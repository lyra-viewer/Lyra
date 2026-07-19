cmake_minimum_required(VERSION 3.10)
project(JxlWrapper LANGUAGES C CXX)

add_library(jxl_native SHARED jxl_native.cpp)

set_target_properties(jxl_native PROPERTIES
        CXX_STANDARD 17
        CXX_STANDARD_REQUIRED YES
        CXX_EXTENSIONS NO
)

# Cross-platform symbol visibility
if (NOT WIN32)
    target_compile_options(jxl_native PRIVATE -fvisibility=hidden)
endif ()

# --- Prefer pkg-config (Homebrew-friendly) ---
find_package(PkgConfig QUIET)
if (PkgConfig_FOUND)
    pkg_check_modules(JXL QUIET libjxl)
    pkg_check_modules(JXL_THREADS QUIET libjxl_threads)
endif ()

if (JXL_FOUND AND JXL_THREADS_FOUND)
    message(STATUS "libjxl found via pkg-config: ${JXL_VERSION}")
    
    if (WIN32)
        list(REMOVE_ITEM JXL_LIBRARIES m)
        list(REMOVE_ITEM JXL_THREADS_LIBRARIES m)
    endif ()

    target_include_directories(jxl_native PRIVATE ${JXL_INCLUDE_DIRS} ${JXL_THREADS_INCLUDE_DIRS})
    target_link_directories(jxl_native PRIVATE ${JXL_LIBRARY_DIRS} ${JXL_THREADS_LIBRARY_DIRS})
    target_link_libraries(jxl_native PRIVATE ${JXL_LIBRARIES} ${JXL_THREADS_LIBRARIES})
    target_compile_options(jxl_native PRIVATE ${JXL_CFLAGS_OTHER})

else ()
    # --- Fallback: CMake config package (vcpkg, libjxl install tree, etc.) ---
    find_package(libjxl CONFIG REQUIRED)

    if (TARGET jxl AND TARGET jxl_threads)
        target_link_libraries(jxl_native PRIVATE jxl jxl_threads)
    else ()
        message(FATAL_ERROR "libjxl found, but expected targets 'jxl' and 'jxl_threads' are missing.")
    endif ()
endif ()
