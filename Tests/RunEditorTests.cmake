# Runs the editor's C# tests inside the editor in batch mode on a fresh copy of a project (cmake -P):
#   -DEDITOR=<HelixEditor> -DPROJECT=<folder> -DWORK=<folder the copy and the log go to> -DMETHOD=<Namespace.Class.Method>
#   [-DWINDOWED=1: with the editor's window (no batch mode), for the tests of its own windows]
# The tests save layouts into the project's UserSettings: the copy keeps the source tree clean. The editor must end with 0.
cmake_minimum_required(VERSION 3.28)

file(REMOVE_RECURSE "${WORK}")
file(MAKE_DIRECTORY "${WORK}")
file(COPY "${PROJECT}/" DESTINATION "${WORK}/Project")
set(log "${WORK}/Editor.log")
if(WINDOWED)
    set(mode "")
else()
    set(mode -batchmode)
endif()
execute_process(COMMAND "${EDITOR}" ${mode} -screen-width 960 -screen-height 540 -projectPath "${WORK}/Project" -executeMethod "${METHOD}"
                        -logFile "${log}"
                RESULT_VARIABLE code OUTPUT_VARIABLE output ERROR_VARIABLE errors TIMEOUT 300)
if(EXISTS "${log}")
    file(READ "${log}" text)
endif()
if(NOT code EQUAL 0)
    message(FATAL_ERROR "The editor's tests ended with ${code}:\n${output}\n${errors}\n${text}")
endif()
string(REGEX MATCH "Editor tests: [^\n]*" summary "${text}")
message(STATUS "${summary}")
