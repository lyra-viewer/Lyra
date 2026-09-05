cmake_minimum_required(VERSION 3.10)
project(TiffWrapper LANGUAGES C CXX)

add_library(tiff_native SHARED tiff_native.cpp tiff_native.h)

set_target_properties(tiff_native PROPERTIES
  CXX_STANDARD 11
  CXX_STANDARD_REQUIRED YES
  CXX_EXTENSIONS NO
)

# Cross-platform symbol visibility
if(NOT WIN32)
  target_compile_options(tiff_native PRIVATE -fvisibility=hidden)
endif()

# --- Prefer pkg-config (Homebrew-friendly) ---
find_package(PkgConfig QUIET)
if(PkgConfig_FOUND)
  pkg_check_modules(TIFF QUIET libtiff-4)
endif()

if(TIFF_FOUND)
  message(STATUS "libtiff found via pkg-config: ${TIFF_VERSION}")

  target_include_directories(tiff_native PRIVATE ${TIFF_INCLUDE_DIRS})
  target_link_directories(tiff_native PRIVATE ${TIFF_LIBRARY_DIRS})
  target_link_libraries(tiff_native PRIVATE ${TIFF_LIBRARIES})
  target_compile_options(tiff_native PRIVATE ${TIFF_CFLAGS_OTHER})

else()
  # --- Fallback: CMake's bundled FindTIFF (works with CMAKE_PREFIX_PATH=$(brew --prefix)) ---
  find_package(TIFF REQUIRED)
  target_link_libraries(tiff_native PRIVATE TIFF::TIFF)
endif()
