cmake_minimum_required(VERSION 3.10)
project(HDRWrapper)

set(SOURCES hdr_native.cpp rgbe.c rgbe.h)
add_library(hdr_native SHARED ${SOURCES})
target_include_directories(hdr_native PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

# Cross-platform symbol visibility
if(NOT WIN32)
    target_compile_options(hdr_native PRIVATE -fvisibility=hidden)
endif()
