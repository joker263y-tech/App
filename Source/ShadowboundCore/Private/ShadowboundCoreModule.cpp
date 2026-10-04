#include "Modules/ModuleManager.h"

// The only file in this module permitted to include an engine header, and the
// only one excluded from the standalone C++ test build (Tools/test-core-cpp.sh).
// Everything else in ShadowboundCore is standard-library-only game logic, which
// is what lets that logic be compiled and tested without Unreal installed.
IMPLEMENT_MODULE(FDefaultModuleImpl, ShadowboundCore)
