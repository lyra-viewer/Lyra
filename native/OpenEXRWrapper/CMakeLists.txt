cmake_minimum_required(VERSION 3.13)
project(OpenEXRWrapper)

# Standard settings
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Link OpenEXR
find_package(OpenEXR REQUIRED)

add_library(exr_native SHARED exr_native.cpp)
target_link_libraries(exr_native PRIVATE OpenEXR::OpenEXR)

if(APPLE)
  # Make our wrapper itself have an @rpath install name
  set_target_properties(exr_native PROPERTIES
    MACOSX_RPATH ON
    INSTALL_NAME_DIR "@rpath"
  )
endif()

# Set export visibility for non-Windows
if(NOT WIN32)
  target_compile_options(exr_native PRIVATE -fvisibility=hidden)
endif()
