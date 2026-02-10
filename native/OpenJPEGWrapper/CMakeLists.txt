cmake_minimum_required(VERSION 3.10)
project(OpenJpegWrapper LANGUAGES C CXX)

add_library(j2k_native SHARED j2k_native.cpp)

set_target_properties(j2k_native PROPERTIES
  CXX_STANDARD 11
  CXX_STANDARD_REQUIRED YES
  CXX_EXTENSIONS NO
)

# Cross-platform symbol visibility
if(NOT WIN32)
  target_compile_options(j2k_native PRIVATE -fvisibility=hidden)
endif()

# --- Prefer pkg-config (Homebrew-friendly) ---
find_package(PkgConfig QUIET)
if(PkgConfig_FOUND)
  pkg_check_modules(OPENJPEG QUIET libopenjp2)
endif()

if(OPENJPEG_FOUND)
  message(STATUS "OpenJPEG found via pkg-config: ${OPENJPEG_VERSION}")

  target_include_directories(j2k_native PRIVATE ${OPENJPEG_INCLUDE_DIRS})
  target_link_directories(j2k_native PRIVATE ${OPENJPEG_LIBRARY_DIRS})
  target_link_libraries(j2k_native PRIVATE ${OPENJPEG_LIBRARIES})
  target_compile_options(j2k_native PRIVATE ${OPENJPEG_CFLAGS_OTHER})

else()
  # --- Fallback: try CMake config package (vcpkg, etc.) ---
  find_package(OpenJPEG CONFIG REQUIRED)

  if(TARGET OpenJPEG::openjp2)
    target_link_libraries(j2k_native PRIVATE OpenJPEG::openjp2)
  elseif(TARGET openjp2)
    target_link_libraries(j2k_native PRIVATE openjp2)
  else()
    message(FATAL_ERROR "OpenJPEG found, but no usable target exported (expected OpenJPEG::openjp2 or openjp2).")
  endif()
endif()
