include(${CMAKE_CURRENT_LIST_DIR}/../shared.cmake)

set(VCPKG_TARGET_ARCHITECTURE arm64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE static)
set(VCPKG_BUILD_TYPE release)

set(VCPKG_CMAKE_SYSTEM_NAME Linux)
set(VCPKG_CHAINLOAD_TOOLCHAIN_FILE ${CMAKE_CURRENT_LIST_DIR}/../toolchains/linux-gcc.cmake)

if(PORT IN_LIST _PKG_LIBS)
  set(VCPKG_LIBRARY_LINKAGE dynamic)
  set(VCPKG_FIXUP_ELF_RPATH ON)
endif()

if(NOT CMAKE_HOST_SYSTEM_PROCESSOR)
  execute_process(COMMAND "uname" "-m" OUTPUT_VARIABLE CMAKE_HOST_SYSTEM_PROCESSOR OUTPUT_STRIP_TRAILING_WHITESPACE)
endif()
