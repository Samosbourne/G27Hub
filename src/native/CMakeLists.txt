cmake_minimum_required(VERSION 3.20)
project(g27hub_native C)

# Gerado duas vezes por build.ps1: -A x64 e -A Win32 (jogos 32 bits carregam a DLL x86).
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded") # CRT estatica: a DLL roda dentro de qualquer jogo
add_compile_definitions(UNICODE _UNICODE _CRT_SECURE_NO_WARNINGS)
add_compile_options(/W4 /WX /wd4100 /wd4201)

add_library(g27ffb SHARED
    g27ffb/g27ffb.c g27ffb/effects.c g27ffb/g27hid.c g27ffb/g27cfg.c g27ffb/hub.c g27ffb/g27ffb.def)
target_link_libraries(g27ffb hid setupapi advapi32)

add_executable(g27fftest fftest/fftest.c)
target_include_directories(g27fftest PRIVATE g27ffb)
target_link_libraries(g27fftest dinput8 ole32 user32 shell32)
