#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <d3d10.h>
#include "nvEncodeAPI.h"
#include <atomic>
#include <condition_variable>
#include <cstdio>
#include <deque>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>
#include <cstring>

namespace {
constexpr int BufferCount=12;
struct Slot {
    ID3D11Texture2D* texture=nullptr;
    NV_ENC_REGISTERED_PTR registered=nullptr;
    NV_ENC_INPUT_PTR mapped=nullptr;
    NV_ENC_OUTPUT_PTR output=nullptr;
    HANDLE event=nullptr;
    bool busy=false;
};
struct Encoder {
    HMODULE library=nullptr;
    NV_ENCODE_API_FUNCTION_LIST api={};
    void* session=nullptr;
    ID3D11Device* device=nullptr;
    ID3D11DeviceContext* context=nullptr;
    ID3D10Multithread* multithread=nullptr;
    BOOL previousProtection=FALSE;
    ID3D11Texture2D* source=nullptr;
    NV_ENC_BUFFER_FORMAT format=NV_ENC_BUFFER_FORMAT_UNDEFINED;
    unsigned width=0,height=0;
    int fps=60;
    bool hevc=false;
    unsigned videoContinuity=0,patContinuity=0,pmtContinuity=0;
    Slot slots[BufferCount];
    FILE* file=nullptr;
    HANDLE eosEvent=nullptr;
    std::mutex mutex;
    std::condition_variable changed;
    std::deque<int> pending;
    std::thread worker;
    std::atomic<int> submitted{0},written{0};
    std::atomic<bool> aborting{false},failed{false},eos{false},done{false};
    std::string error;

    void Check(NVENCSTATUS result,const char* operation) {
        if(result==NV_ENC_SUCCESS)return;
        const char* detail=session&&api.nvEncGetLastErrorString?api.nvEncGetLastErrorString(session):"";
        throw std::runtime_error(std::string(operation)+" failed ("+std::to_string(result)+"): "+(detail?detail:""));
    }
    void Fail(const char* message) {
        {std::lock_guard<std::mutex> lock(mutex);if(error.empty())error=message;}
        failed=true;changed.notify_all();
    }
    void Initialize(ID3D11Texture2D* input,int fps,int bitrate,bool hevc,const wchar_t* path) {
        this->fps=fps;this->hevc=hevc;
        source=input;source->AddRef();source->GetDevice(&device);device->GetImmediateContext(&context);
        if(SUCCEEDED(context->QueryInterface(__uuidof(ID3D10Multithread),reinterpret_cast<void**>(&multithread))))
            previousProtection=multithread->SetMultithreadProtected(TRUE);
        D3D11_TEXTURE2D_DESC descriptor;source->GetDesc(&descriptor);
        width=descriptor.Width;height=descriptor.Height;
        if(descriptor.SampleDesc.Count!=1)throw std::runtime_error("NVENC source must be single-sample");
        if(descriptor.Format==DXGI_FORMAT_R8G8B8A8_UNORM||descriptor.Format==DXGI_FORMAT_R8G8B8A8_UNORM_SRGB||descriptor.Format==DXGI_FORMAT_R8G8B8A8_TYPELESS) {
            descriptor.Format=DXGI_FORMAT_R8G8B8A8_UNORM;format=NV_ENC_BUFFER_FORMAT_ABGR;
        } else if(descriptor.Format==DXGI_FORMAT_B8G8R8A8_UNORM||descriptor.Format==DXGI_FORMAT_B8G8R8A8_UNORM_SRGB||descriptor.Format==DXGI_FORMAT_B8G8R8A8_TYPELESS) {
            descriptor.Format=DXGI_FORMAT_B8G8R8A8_UNORM;format=NV_ENC_BUFFER_FORMAT_ARGB;
        } else throw std::runtime_error("NVENC source requires RGBA8/BGRA8");
        library=LoadLibraryW(L"nvEncodeAPI64.dll");
        if(!library)throw std::runtime_error("NVIDIA encode driver not found");
        using CreateInstance=NVENCSTATUS (NVENCAPI*)(NV_ENCODE_API_FUNCTION_LIST*);
        auto create=reinterpret_cast<CreateInstance>(GetProcAddress(library,"NvEncodeAPICreateInstance"));
        if(!create)throw std::runtime_error("NvEncodeAPICreateInstance not found");
        api.version=NV_ENCODE_API_FUNCTION_LIST_VER;Check(create(&api),"CreateInstance");
        NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS open={};open.version=NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS_VER;
        open.device=device;open.deviceType=NV_ENC_DEVICE_TYPE_DIRECTX;open.apiVersion=NVENCAPI_VERSION;
        Check(api.nvEncOpenEncodeSessionEx(&open,&session),"OpenSession");
        GUID codec=hevc?NV_ENC_CODEC_HEVC_GUID:NV_ENC_CODEC_H264_GUID;
        NV_ENC_CAPS_PARAM caps={};caps.version=NV_ENC_CAPS_PARAM_VER;caps.capsToQuery=NV_ENC_CAPS_ASYNC_ENCODE_SUPPORT;
        int asynchronous=0;Check(api.nvEncGetEncodeCaps(session,codec,&caps,&asynchronous),"AsyncCaps");
        if(!asynchronous)throw std::runtime_error("Asynchronous DirectX NVENC is unavailable");
        NV_ENC_PRESET_CONFIG preset={};preset.version=NV_ENC_PRESET_CONFIG_VER;preset.presetCfg.version=NV_ENC_CONFIG_VER;
        Check(api.nvEncGetEncodePresetConfigEx(session,codec,NV_ENC_PRESET_P5_GUID,NV_ENC_TUNING_INFO_HIGH_QUALITY,&preset),"PresetP5");
        NV_ENC_CONFIG config=preset.presetCfg;
        config.gopLength=250;config.frameIntervalP=3;
        config.profileGUID=hevc?NV_ENC_HEVC_PROFILE_MAIN_GUID:NV_ENC_H264_PROFILE_MAIN_GUID;
        config.rcParams.rateControlMode=NV_ENC_PARAMS_RC_VBR;
        config.rcParams.averageBitRate=bitrate*1000000;
        config.rcParams.maxBitRate=bitrate*1500000;
        config.rcParams.vbvBufferSize=bitrate*2000000;
        config.rcParams.enableLookahead=0;
        if(hevc) {config.encodeCodecConfig.hevcConfig.idrPeriod=250;config.encodeCodecConfig.hevcConfig.chromaFormatIDC=1;config.encodeCodecConfig.hevcConfig.repeatSPSPPS=1;}
        else {config.encodeCodecConfig.h264Config.idrPeriod=250;config.encodeCodecConfig.h264Config.chromaFormatIDC=1;config.encodeCodecConfig.h264Config.repeatSPSPPS=1;}
        NV_ENC_INITIALIZE_PARAMS init={};init.version=NV_ENC_INITIALIZE_PARAMS_VER;
        init.encodeGUID=codec;init.presetGUID=NV_ENC_PRESET_P5_GUID;init.tuningInfo=NV_ENC_TUNING_INFO_HIGH_QUALITY;
        init.encodeWidth=init.darWidth=width;init.encodeHeight=init.darHeight=height;
        init.frameRateNum=fps;init.frameRateDen=1;init.enableEncodeAsync=1;init.enablePTD=1;init.encodeConfig=&config;
        Check(api.nvEncInitializeEncoder(session,&init),"InitializeEncoder");
        descriptor.MipLevels=1;descriptor.ArraySize=1;descriptor.Usage=D3D11_USAGE_DEFAULT;
        descriptor.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
        descriptor.CPUAccessFlags=0;descriptor.MiscFlags=0;
        for(auto& slot:slots) {
            if(FAILED(device->CreateTexture2D(&descriptor,nullptr,&slot.texture)))throw std::runtime_error("Create NVENC GPU surface failed");
            NV_ENC_REGISTER_RESOURCE reg={};reg.version=NV_ENC_REGISTER_RESOURCE_VER;
            reg.resourceType=NV_ENC_INPUT_RESOURCE_TYPE_DIRECTX;reg.resourceToRegister=slot.texture;
            reg.width=width;reg.height=height;reg.bufferFormat=format;reg.bufferUsage=NV_ENC_INPUT_IMAGE;
            Check(api.nvEncRegisterResource(session,&reg),"RegisterResource");slot.registered=reg.registeredResource;
            NV_ENC_CREATE_BITSTREAM_BUFFER buffer={};buffer.version=NV_ENC_CREATE_BITSTREAM_BUFFER_VER;
            Check(api.nvEncCreateBitstreamBuffer(session,&buffer),"CreateBitstream");slot.output=buffer.bitstreamBuffer;
            slot.event=CreateEventW(nullptr,FALSE,FALSE,nullptr);
            if(!slot.event)throw std::runtime_error("Create NVENC completion event failed");
            NV_ENC_EVENT_PARAMS event={};event.version=NV_ENC_EVENT_PARAMS_VER;event.completionEvent=slot.event;
            Check(api.nvEncRegisterAsyncEvent(session,&event),"RegisterAsyncEvent");
        }
        eosEvent=CreateEventW(nullptr,FALSE,FALSE,nullptr);
        if(!eosEvent)throw std::runtime_error("Create NVENC EOS event failed");
        NV_ENC_EVENT_PARAMS completion={};completion.version=NV_ENC_EVENT_PARAMS_VER;completion.completionEvent=eosEvent;
        Check(api.nvEncRegisterAsyncEvent(session,&completion),"RegisterEOSEvent");
        file=_wfopen(path,L"wb");if(!file)throw std::runtime_error("Cannot create encoded video file");
        worker=std::thread([this]{Consume();});
    }
    // MPEG-TS transports compressed packets with NVENC's real presentation
    // timestamps. Raw Annex-B alone loses B-frame ordering during remuxing.
    void WritePacket(const unsigned char* packet) {
        if(fwrite(packet,1,188,file)!=188)throw std::runtime_error("Encoded file write failed");
    }
    void WriteTable(unsigned pid,unsigned& continuity,std::vector<unsigned char> section) {
        unsigned crc=0xffffffff;
        for(auto byte:section){crc^=unsigned(byte)<<24;for(int i=0;i<8;i++)crc=(crc<<1)^((crc&0x80000000)?0x04c11db7:0);}
        for(int shift=24;shift>=0;shift-=8)section.push_back(crc>>shift);
        unsigned char packet[188];memset(packet,0xff,sizeof(packet));
        packet[0]=0x47;packet[1]=0x40|(pid>>8);packet[2]=pid;packet[3]=0x10|(continuity++&15);packet[4]=0;
        memcpy(packet+5,section.data(),section.size());WritePacket(packet);
    }
    static void Timestamp(std::vector<unsigned char>& data,unsigned tag,unsigned long long value) {
        data.push_back((tag<<4)|(((value>>30)&7)<<1)|1);
        data.push_back(value>>22);data.push_back((((value>>15)&127)<<1)|1);
        data.push_back(value>>7);data.push_back(((value&127)<<1)|1);
    }
    void WriteVideo(const NV_ENC_LOCK_BITSTREAM& bitstream) {
        int index=written;
        if(index%fps==0) {
            WriteTable(0,patContinuity,{0x00,0xb0,0x0d,0,1,0xc1,0,0,0,1,0xf0,0});
            WriteTable(0x1000,pmtContinuity,{0x02,0xb0,0x12,0,1,0xc1,0,0,0xe1,0,0xf0,0,
                static_cast<unsigned char>(hevc?0x24:0x1b),0xe1,0,0xf0,0});
        }
        unsigned long long pts=(bitstream.outputTimeStamp+2)*90000/fps;
        unsigned long long dts=static_cast<unsigned long long>(index)*90000/fps;
        std::vector<unsigned char> pes={0,0,1,0xe0,0,0,0x80,0xc0,10};
        Timestamp(pes,3,pts);Timestamp(pes,1,dts);
        auto* bytes=static_cast<unsigned char*>(bitstream.bitstreamBufferPtr);
        pes.insert(pes.end(),bytes,bytes+bitstream.bitstreamSizeInBytes);
        size_t offset=0;
        while(offset<pes.size()) {
            bool first=offset==0;
            size_t available=first?176:184;
            size_t payload=std::min(available,pes.size()-offset);
            bool adaptation=first||payload<184;
            unsigned char packet[188];memset(packet,0xff,sizeof(packet));
            packet[0]=0x47;packet[1]=(first?0x40:0)|1;packet[2]=0;
            packet[3]=(adaptation?0x30:0x10)|(videoContinuity++&15);
            size_t start=4;
            if(adaptation) {
                size_t length=183-payload;packet[4]=length;start+=length+1;
                if(length){packet[5]=first?0x10:0;}
                if(first) {
                    packet[6]=dts>>25;packet[7]=dts>>17;packet[8]=dts>>9;packet[9]=dts>>1;
                    packet[10]=((dts&1)<<7)|0x7e;packet[11]=0;
                }
            }
            memcpy(packet+start,pes.data()+offset,payload);WritePacket(packet);offset+=payload;
        }
    }
    // Called only by Unity's render thread, after the input texture's GPU blit.
    void Submit() {
        if(failed||aborting||eos)return;
        int index=submitted%BufferCount;auto& slot=slots[index];
        {std::unique_lock<std::mutex> lock(mutex);changed.wait(lock,[&]{return !slot.busy||failed||aborting;});
         if(failed||aborting)return;slot.busy=true;}
        if(slot.mapped){Check(api.nvEncUnmapInputResource(session,slot.mapped),"UnmapInput");slot.mapped=nullptr;}
        context->CopyResource(slot.texture,source);context->Flush();
        NV_ENC_MAP_INPUT_RESOURCE map={};map.version=NV_ENC_MAP_INPUT_RESOURCE_VER;map.registeredResource=slot.registered;
        Check(api.nvEncMapInputResource(session,&map),"MapInput");slot.mapped=map.mappedResource;
        NV_ENC_PIC_PARAMS pic={};pic.version=NV_ENC_PIC_PARAMS_VER;
        pic.inputBuffer=slot.mapped;pic.bufferFmt=format;pic.inputWidth=width;pic.inputHeight=height;
        pic.outputBitstream=slot.output;pic.completionEvent=slot.event;pic.pictureStruct=NV_ENC_PIC_STRUCT_FRAME;
        pic.inputTimeStamp=submitted;pic.inputDuration=1;
        auto result=api.nvEncEncodePicture(session,&pic);
        if(result!=NV_ENC_SUCCESS&&result!=NV_ENC_ERR_NEED_MORE_INPUT)Check(result,"EncodePicture");
        {std::lock_guard<std::mutex> lock(mutex);pending.push_back(index);submitted++;}
        changed.notify_all();
    }
    void Finish() {
        if(eos||failed||aborting)return;
        NV_ENC_PIC_PARAMS pic={};pic.version=NV_ENC_PIC_PARAMS_VER;pic.encodePicFlags=NV_ENC_PIC_FLAG_EOS;pic.completionEvent=eosEvent;
        Check(api.nvEncEncodePicture(session,&pic),"EncodeEOS");eos=true;changed.notify_all();
    }
    void Consume() {
        try {
            for(;;) {
                int index;
                {std::unique_lock<std::mutex> lock(mutex);changed.wait(lock,[&]{return !pending.empty()||eos||failed||aborting;});
                 if(failed||aborting)break;if(pending.empty()){if(eos)break;continue;}index=pending.front();pending.pop_front();}
                auto& slot=slots[index];
                for(;;) {
                    DWORD wait=WaitForSingleObject(slot.event,100);
                    if(failed||aborting)return;
                    if(wait==WAIT_OBJECT_0)break;
                    if(wait!=WAIT_TIMEOUT)throw std::runtime_error("NVENC completion wait failed");
                }
                NV_ENC_LOCK_BITSTREAM bitstream={};bitstream.version=NV_ENC_LOCK_BITSTREAM_VER;
                bitstream.outputBitstream=slot.output;bitstream.doNotWait=1;
                Check(api.nvEncLockBitstream(session,&bitstream),"LockBitstream");
                try {WriteVideo(bitstream);}
                catch(...){api.nvEncUnlockBitstream(session,slot.output);throw;}
                Check(api.nvEncUnlockBitstream(session,slot.output),"UnlockBitstream");
                {std::lock_guard<std::mutex> lock(mutex);slot.busy=false;written++;}changed.notify_all();
            }
            if(file){if(fflush(file)!=0)throw std::runtime_error("Encoded file flush failed");}
            done=true;
        }catch(const std::exception& error){Fail(error.what());}
    }
    ~Encoder() {
        aborting=true;changed.notify_all();if(worker.joinable())worker.join();
        if(session) {
            if(eosEvent){NV_ENC_EVENT_PARAMS event={};event.version=NV_ENC_EVENT_PARAMS_VER;event.completionEvent=eosEvent;api.nvEncUnregisterAsyncEvent(session,&event);}
            for(auto& slot:slots) {
                if(slot.mapped)api.nvEncUnmapInputResource(session,slot.mapped);
                if(slot.registered)api.nvEncUnregisterResource(session,slot.registered);
                if(slot.output)api.nvEncDestroyBitstreamBuffer(session,slot.output);
                if(slot.event){NV_ENC_EVENT_PARAMS event={};event.version=NV_ENC_EVENT_PARAMS_VER;event.completionEvent=slot.event;api.nvEncUnregisterAsyncEvent(session,&event);}
            }
            api.nvEncDestroyEncoder(session);
        }
        for(auto& slot:slots){if(slot.event)CloseHandle(slot.event);if(slot.texture)slot.texture->Release();}
        if(eosEvent)CloseHandle(eosEvent);if(file)fclose(file);
        if(multithread){multithread->SetMultithreadProtected(previousProtection);multithread->Release();}
        if(source)source->Release();if(context)context->Release();if(device)device->Release();if(library)FreeLibrary(library);
    }
};
thread_local std::string lastError;
void __stdcall RenderEvent(int operation,void* data) {
    auto* encoder=static_cast<Encoder*>(data);if(!encoder)return;
    try {if(operation==1)encoder->Submit();else if(operation==2)encoder->Finish();}
    catch(const std::exception& error){encoder->Fail(error.what());}
}
}
#define API extern "C" __declspec(dllexport)
API void* UVVideo_Create(void* texture,int fps,int bitrate,int hevc,const wchar_t* path) {
    auto* encoder=new Encoder;
    try {encoder->Initialize(static_cast<ID3D11Texture2D*>(texture),fps,bitrate,hevc!=0,path);return encoder;}
    catch(const std::exception& error){lastError=error.what();delete encoder;return nullptr;}
}
API void* UVVideo_GetRenderEvent(){return reinterpret_cast<void*>(&RenderEvent);}
API const char* UVVideo_Error(void* handle) {
    if(handle){auto* e=static_cast<Encoder*>(handle);std::lock_guard<std::mutex> lock(e->mutex);lastError=e->error;}
    return lastError.c_str();
}
API int UVVideo_State(void* handle,int* submitted,int* written) {
    auto* e=static_cast<Encoder*>(handle);*submitted=e->submitted;*written=e->written;
    return e->failed?-1:e->done?1:0;
}
API void UVVideo_Destroy(void* handle){delete static_cast<Encoder*>(handle);}
