cmake_minimum_required(VERSION 3.10)
project(BasisWrapper LANGUAGES CXX)

# The shim plus the vendored Binomial transcoder (transcoder/ only; the encoder is not needed).
add_library(basis_native SHARED
  basis_native.cpp
  basis_native.h
  basis_universal/transcoder/basisu_transcoder.cpp
)

set_target_properties(basis_native PROPERTIES
  CXX_STANDARD 17
  CXX_STANDARD_REQUIRED YES
  CXX_EXTENSIONS NO
)

# KTX2 support + Zstd (UASTC in KTX2 is frequently Zstd-supercompressed). The transcoder #errors if
# BASISD_SUPPORT_KTX2_ZSTD is left undefined, so both are set explicitly.
target_compile_definitions(basis_native PRIVATE
  BASISD_SUPPORT_KTX2=1
  BASISD_SUPPORT_KTX2_ZSTD=1
)

if(NOT WIN32)
  target_compile_options(basis_native PRIVATE -fvisibility=hidden -fvisibility-inlines-hidden)
endif()

find_package(PkgConfig QUIET)
if(PkgConfig_FOUND)
  pkg_check_modules(ZSTD QUIET libzstd)
endif()

if(ZSTD_FOUND)
  message(STATUS "libzstd found via pkg-config: ${ZSTD_VERSION}")
  target_include_directories(basis_native PRIVATE ${ZSTD_INCLUDE_DIRS})
  target_link_directories(basis_native PRIVATE ${ZSTD_LIBRARY_DIRS})
  target_link_libraries(basis_native PRIVATE ${ZSTD_LIBRARIES})
  target_compile_options(basis_native PRIVATE ${ZSTD_CFLAGS_OTHER})
else()
  # Fallback: locate libzstd by hand (works with CMAKE_PREFIX_PATH=$(brew --prefix)).
  find_path(ZSTD_INCLUDE_DIR zstd.h)
  find_library(ZSTD_LIBRARY NAMES zstd)
  if(NOT ZSTD_INCLUDE_DIR OR NOT ZSTD_LIBRARY)
    message(FATAL_ERROR "libzstd not found; install it (brew install zstd) or set CMAKE_PREFIX_PATH")
  endif()
  target_include_directories(basis_native PRIVATE ${ZSTD_INCLUDE_DIR})
  target_link_libraries(basis_native PRIVATE ${ZSTD_LIBRARY})
endif()
