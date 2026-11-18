# Runs the editor in batch mode on a pipeline and compares its screenshot with the golden image (cmake -P):
#   -DEDITOR=<HelixEditor> -DCOMPARE=<CompareImages> -DPROJECT=<folder> -DPIPELINE=<name> -DGOLDEN=<png> -DOUT=<png>
#   [-DGRAPHICS=<a player argument choosing the device, e.g. -force-glcore>] [-DDEVICE=<adapter index>]
#   [-DMACHINE_FONTS=1: drawn with the machine's fonts, compared only off CI]
# The launcher must end with 0 and the images must match (HELIX_UPDATE_GOLDEN=1 writes the golden image).
cmake_minimum_required(VERSION 3.28)

get_filename_component(out_dir "${OUT}" DIRECTORY)
file(MAKE_DIRECTORY "${out_dir}")
file(REMOVE "${OUT}")
set(log "${OUT}.log")
# A copy of the project (the editor writes its .meta files and layouts).
# Its folder's name kept: the window's title shows it.
get_filename_component(shot_name "${OUT}" NAME_WE)
get_filename_component(project_name "${PROJECT}" NAME)
set(project "${out_dir}/${shot_name}-work/${project_name}")
file(REMOVE_RECURSE "${out_dir}/${shot_name}-work")
file(COPY "${PROJECT}/" DESTINATION "${project}")
set(arguments -batchmode -frames 8 -screen-width 960 -screen-height 540 -pipeline "${PIPELINE}" -projectPath "${project}" -screenshot "${OUT}" -logFile "${log}")
if(GRAPHICS)
    list(APPEND arguments ${GRAPHICS})
endif()
if(DEFINED DEVICE)
    list(APPEND arguments -force-device-index ${DEVICE})
endif()
execute_process(COMMAND "${EDITOR}" ${arguments} RESULT_VARIABLE code OUTPUT_VARIABLE output ERROR_VARIABLE errors TIMEOUT 300)
if(NOT code EQUAL 0)
    if(EXISTS "${log}")
        file(READ "${log}" text)
    endif()
    message(FATAL_ERROR "The editor on ${PIPELINE} ended with ${code}:\n${output}\n${errors}\n${text}")
endif()
if(MACHINE_FONTS AND DEFINED ENV{CI})
    message(STATUS "The editor on ${PIPELINE} ran; its screenshot depends on the machine's fonts and is not compared on CI.")
    return()
endif()
execute_process(COMMAND "${COMPARE}" "${GOLDEN}" "${OUT}" RESULT_VARIABLE compared OUTPUT_VARIABLE report)
message(STATUS "The editor on ${PIPELINE}: ${report}")
if(NOT compared EQUAL 0)
    message(FATAL_ERROR "The editor on ${PIPELINE} does not match ${GOLDEN} (the screenshot is ${OUT}).")
endif()
