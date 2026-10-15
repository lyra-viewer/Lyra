cmake_minimum_required(VERSION 3.10)
project(LibRawWrapper LANGUAGES C CXX)

add_library(raw_native SHARED raw_native.cpp raw_native.h)

set_target_properties(raw_native PROPERTIES
        CXX_STANDARD 17
        CXX_STANDARD_REQUIRED YES
        CXX_EXTENSIONS NO
)

# Cross-platform symbol visibility
if (NOT WIN32)
    target_compile_options(raw_native PRIVATE -fvisibility=hidden)
endif ()

# LibRaw's own transitive libraries are its business, not ours.
if (APPLE)
    target_link_options(raw_native PRIVATE -Wl,-dead_strip_dylibs)
elseif (UNIX)
    target_link_options(raw_native PRIVATE -Wl,--as-needed)
endif ()

# --- Prefer pkg-config (Homebrew-friendly) ---
find_package(PkgConfig QUIET)
if (PkgConfig_FOUND)
    pkg_check_modules(LIBRAW QUIET libraw_r)
endif ()

if (LIBRAW_FOUND)
    message(STATUS "LibRaw found via pkg-config: ${LIBRAW_VERSION}")

    # Only the libraries: the .pc's other link flags ask for OpenMP, which the wrapper does not use.
    target_include_directories(raw_native PRIVATE ${LIBRAW_INCLUDE_DIRS})
    target_link_directories(raw_native PRIVATE ${LIBRAW_LIBRARY_DIRS})
    target_link_libraries(raw_native PRIVATE ${LIBRAW_LIBRARIES})

else ()
    # --- Fallback: CMake config package (vcpkg, LibRaw install tree, etc.) ---
    find_package(libraw CONFIG REQUIRED)

    if (TARGET libraw::raw_r)
        target_link_libraries(raw_native PRIVATE libraw::raw_r)
    else ()
        message(FATAL_ERROR "LibRaw found, but the expected target 'libraw::raw_r' is missing.")
    endif ()
endif ()
