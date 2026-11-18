cmake_minimum_required(VERSION 3.28)
project(Helix.Editor LANGUAGES CXX)

if(NOT COMMAND helix_add_library)
    include("${CMAKE_CURRENT_SOURCE_DIR}/External/Helix.CMake/Helix.cmake")
endif()

# The editor: the host HelixEditor (the player core on a window with the editor's own frame, both render pipelines, the
# UI and the canvas UI linked in) with its native module HelixEditor (menus, the main toolbar, the editor's application
# and preferences) and its C# side HelixEditor.CoreModule (the editor written in C#). Its data folder (Data/: the shell,
# the skin, the scene) is read where it is. Desktop only.
helix_require(Core)
helix_require(Engine)
helix_require(RenderPipelines.Standard)
helix_require(RenderPipelines.Physical)
helix_require(UI)
helix_require(CanvasUI)
helix_require(Player)
include("${HELIX_BINDGEN_ROOT}/CMake/HelixBindgen.cmake")

if(HELIX_PLATFORM STREQUAL "windows" OR HELIX_PLATFORM STREQUAL "linux")
    set(editor_source "${CMAKE_CURRENT_SOURCE_DIR}/Source")
    helix_collect_sources(editor_sources "${editor_source}/Helix")
    file(GLOB_RECURSE editor_headers CONFIGURE_DEPENDS "${editor_source}/Helix/*.h")
    helix_add_executable(helix_editor SOURCES ${editor_sources} ${editor_headers}
                         DEPS Helix::PlayerCore Helix::RenderPipelines.Standard Helix::RenderPipelines.Physical Helix::UIEngine Helix::CanvasUI)
    set_target_properties(helix_editor PROPERTIES OUTPUT_NAME HelixEditor RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}")
    target_include_directories(helix_editor PRIVATE "${editor_source}")
    target_compile_definitions(helix_editor PRIVATE "HELIX_EDITOR_DATA_DIR=\"${CMAKE_CURRENT_SOURCE_DIR}/Data\"")

    # The native module HelixEditor: its tagged headers bound to C#.
    helix_dependency_dir(editor_engine_dir Engine)
    helix_dependency_dir(editor_math_dir Math)
    helix_dependency_dir(editor_platform_dir Platform)
    set(editor_engine_source "${editor_engine_dir}/Source")
    file(GLOB_RECURSE editor_bound_headers CONFIGURE_DEPENDS "${editor_source}/Helix/Editor/*.h")
    list(FILTER editor_bound_headers EXCLUDE REGEX "/(Implementation|Host)/")
    set(editor_references "${editor_engine_source}/Helix/Engine/Object/EntityId.h" "${editor_engine_source}/Helix/Engine/Object/Object.h"
        "${editor_math_dir}/Source/Helix/Math/Vector.h" "${editor_math_dir}/Source/Helix/Math/Geometry.h")
    helix_bindgen(helix_editor MODULE HelixEditor NAMESPACE HelixEditor KIND NativeGame HEADERS ${editor_bound_headers}
                  REFERENCES ${editor_references} REFERENCE_NAMESPACE HelixEngine
                  INCLUDE_ROOTS "${editor_source}" "${editor_engine_source}" "${editor_math_dir}/Source" "${editor_platform_dir}/Source"
                  CS_OUTPUT editor_cs)

    # The editor's C# side, built beside the host into Managed/ with the engine's assemblies (the folder the host gives
    # the player).
    set(editor_managed "${CMAKE_BINARY_DIR}/Managed")
    set(editor_artifacts "${CMAKE_BINARY_DIR}/HelixEditorManaged")
    set(editor_core_dll "${editor_artifacts}/bin/HelixEditor.CoreModule/release/HelixEditor.CoreModule.dll")
    set(editor_core_dir "${editor_source}/HelixEditor.CoreModule")
    file(GLOB_RECURSE editor_cs_sources CONFIGURE_DEPENDS "${editor_core_dir}/*.cs" "${editor_core_dir}/*.csproj")
    list(FILTER editor_cs_sources EXCLUDE REGEX "/(bin|obj)/")
    get_filename_component(editor_runtime_dir "${HELIX_SCRIPTING_RUNTIME}" DIRECTORY)
    get_filename_component(editor_runtime_name "${HELIX_SCRIPTING_RUNTIME}" NAME_WLE)
    set(editor_engine_assemblies "${HELIX_SCRIPTING_RUNTIME}" "${HELIX_CORE_MODULE}" ${HELIX_CORE_MODULE_REFERENCES} "${HELIX_UI_RUNTIME}"
        "${HELIX_CANVASUI_RUNTIME}")
    add_custom_command(
        OUTPUT "${editor_managed}/.staged"
        COMMAND "${HELIX_DOTNET}" build "${editor_core_dir}/HelixEditor.CoreModule.csproj" -c Release --artifacts-path "${editor_artifacts}" --nologo -v quiet
                "-p:HelixCoreModule=${HELIX_CORE_MODULE}" "-p:HelixScriptingRuntime=${HELIX_SCRIPTING_RUNTIME}" "-p:HelixUIRuntime=${HELIX_UI_RUNTIME}"
                "-p:HelixModuleBindings=${editor_cs}"
        COMMAND "${CMAKE_COMMAND}" -E make_directory "${editor_managed}"
        COMMAND "${CMAKE_COMMAND}" -E copy "${editor_core_dll}" ${editor_engine_assemblies} "${editor_runtime_dir}/${editor_runtime_name}.runtimeconfig.json"
                "${editor_managed}"
        COMMAND "${CMAKE_COMMAND}" -E touch "${editor_managed}/.staged"
        DEPENDS ${editor_cs_sources} ${editor_engine_assemblies} "${editor_cs}"
        COMMENT "Building HelixEditor.CoreModule")
    add_custom_target(helix_editor_managed ALL DEPENDS "${editor_managed}/.staged")
    add_dependencies(helix_editor_managed helix_ui_runtime helix_canvasui_runtime helix_core_module helix_scripting_runtime helix_editor)
    add_dependencies(helix_editor helix_core_module)

    # The shader assemblies beside the host: the core's library and shaders, both pipelines', the UI's, the canvas UI's.
    set(editor_shaders_out "${CMAKE_BINARY_DIR}/Shaders")
    set(editor_shader_assemblies "${HELIX_RENDER_PIPELINES_CORE_SHADER_LIBRARY}" "${HELIX_RENDER_PIPELINES_CORE_SHADERS}"
        "${HELIX_RENDER_PIPELINES_STANDARD_SHADERS}" "${HELIX_RENDER_PIPELINES_PHYSICAL_SHADERS}" "${HELIX_UI_SHADERS}" "${HELIX_CANVASUI_SHADERS}")
    add_custom_command(
        OUTPUT "${editor_shaders_out}/.staged"
        COMMAND "${CMAKE_COMMAND}" -E make_directory "${editor_shaders_out}"
        COMMAND "${CMAKE_COMMAND}" -E copy ${editor_shader_assemblies} "${editor_shaders_out}"
        COMMAND "${CMAKE_COMMAND}" -E touch "${editor_shaders_out}/.staged"
        DEPENDS ${editor_shader_assemblies}
        COMMENT "Staging the editor's shader assemblies")
    add_custom_target(helix_editor_shaders ALL DEPENDS "${editor_shaders_out}/.staged")
    add_dependencies(helix_editor_shaders helix_render_pipelines_core_shaders helix_render_pipelines_standard_shaders
                     helix_render_pipelines_physical_shaders helix_ui_shaders helix_canvasui_shaders)
    add_dependencies(helix_editor helix_editor_shaders)

    if(HELIX_BUILD_TESTS AND PROJECT_IS_TOP_LEVEL)
        # The registries and the preferences in the engine.
        helix_add_test_suite(helix_editor_tests DEPS Helix::EngineShared Helix::Platform)
        target_sources(helix_editor_tests PRIVATE "${editor_source}/Helix/Editor/Shell/Implementation/EditorMenus.cpp"
                       "${editor_source}/Helix/Editor/Shell/Implementation/EditorToolbar.cpp"
                       "${editor_source}/Helix/Editor/Application/Implementation/EditorPrefs.cpp")
        target_include_directories(helix_editor_tests PRIVATE "${editor_source}")
        # The editor in batch mode, its screenshot against Tests/Golden (drawn with the machine's fonts: compared only
        # off CI).
        helix_add_executable(helix_editor_compare SOURCES "${CMAKE_CURRENT_SOURCE_DIR}/Tools/CompareImages/CompareImages.cpp" DEPS Helix::Image Helix::Platform)
        set_target_properties(helix_editor_compare PROPERTIES OUTPUT_NAME CompareImages)
        # The editor's C# tests (Tests/Managed), built into Managed/ beside the editor's assemblies and run inside it.
        set(editor_tests_dir "${CMAKE_CURRENT_SOURCE_DIR}/Tests/Managed")
        set(editor_tests_dll "${editor_artifacts}/bin/HelixEditor.Tests/release/HelixEditor.Tests.dll")
        file(GLOB editor_tests_sources CONFIGURE_DEPENDS "${editor_tests_dir}/*.cs" "${editor_tests_dir}/*.csproj")
        add_custom_command(
            OUTPUT "${editor_managed}/HelixEditor.Tests.dll"
            COMMAND "${HELIX_DOTNET}" build "${editor_tests_dir}/HelixEditor.Tests.csproj" -c Release --artifacts-path "${editor_artifacts}" --nologo -v quiet
                    "-p:HelixCoreModule=${HELIX_CORE_MODULE}" "-p:HelixScriptingRuntime=${HELIX_SCRIPTING_RUNTIME}" "-p:HelixUIRuntime=${HELIX_UI_RUNTIME}"
                    "-p:HelixEditorCoreModule=${editor_core_dll}"
            COMMAND "${CMAKE_COMMAND}" -E copy "${editor_tests_dll}" "${editor_managed}"
            DEPENDS ${editor_tests_sources} "${editor_managed}/.staged"
            COMMENT "Building HelixEditor.Tests")
        add_custom_target(helix_editor_managed_tests ALL DEPENDS "${editor_managed}/HelixEditor.Tests.dll")
        add_dependencies(helix_editor_managed_tests helix_editor_managed)
        add_test(NAME helix_editor_csharp
                 COMMAND "${CMAKE_COMMAND}" "-DEDITOR=$<TARGET_FILE:helix_editor>" "-DPROJECT=${CMAKE_CURRENT_SOURCE_DIR}/Tests/Projects/Empty"
                         "-DWORK=${CMAKE_CURRENT_BINARY_DIR}/csharp" "-DMETHOD=HelixEditor.Tests.EditorTests.Run"
                         -P "${CMAKE_CURRENT_SOURCE_DIR}/Tests/RunEditorTests.cmake")
        foreach(pipeline IN ITEMS standard physical)
            add_test(NAME helix_editor_shell_${pipeline}
                     COMMAND "${CMAKE_COMMAND}" "-DEDITOR=$<TARGET_FILE:helix_editor>" "-DCOMPARE=$<TARGET_FILE:helix_editor_compare>"
                             "-DPIPELINE=${pipeline}" "-DMACHINE_FONTS=1" "-DPROJECT=${CMAKE_CURRENT_SOURCE_DIR}/Tests/Projects/Empty"
                             "-DGOLDEN=${CMAKE_CURRENT_SOURCE_DIR}/Tests/Golden/Shell-${pipeline}.png"
                             "-DOUT=${CMAKE_CURRENT_BINARY_DIR}/shots/Shell-${pipeline}.png" -P "${CMAKE_CURRENT_SOURCE_DIR}/Tests/RunEditor.cmake")
        endforeach()
        # The .NET runtime is never unloaded: LeakSanitizer is told about its modules.
        if(HELIX_ENABLE_ASAN AND HELIX_PLATFORM STREQUAL "linux")
            helix_dependency_dir(editor_scripting_dir Scripting)
            set_tests_properties(helix_editor_csharp helix_editor_shell_standard helix_editor_shell_physical PROPERTIES
                                 ENVIRONMENT "LSAN_OPTIONS=suppressions=${editor_scripting_dir}/Tests/Support/LeakSuppressions.txt")
        endif()
    endif()
endif()
