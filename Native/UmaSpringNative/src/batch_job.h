#pragma once
#include <cstdint>
#include <cstddef>

// Private UmaSpringNative x64 ABI. Jobs are consumed sequentially and may
// deliberately share cloth/history, collision, parent and skirt buffers.
struct NativeSpringBatchJob {
 float* cloth;
 void* collisions;
 void* parents;
 float* skirt;
 float* skirtArgs;
 std::int32_t count;
 std::uint32_t flags; // bit 0: collision enabled; bit 1: 60fps solver branch
 float stiffness,drag,gravity,windX,windY,windZ,windStrength;
 float timescale,move,addMove,spring;
};
static_assert(sizeof(NativeSpringBatchJob)==96,"Native batch job ABI size");
static_assert(offsetof(NativeSpringBatchJob,stiffness)==48,"Native batch rate offset");
static_assert(offsetof(NativeSpringBatchJob,timescale)==76,"Native batch time offset");
