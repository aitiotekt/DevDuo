"""Ecosystem entry point; each command owns its argument parser."""

import argparse
import importlib
import subprocess
import sys

COMMANDS = {
    "icons": ("icons", "main", "Export, compile or validate application icon resources"),
    "serve": ("calibration_server", "main", "Start the loopback calibration receiver"),
    "capture-macos": ("capture_macos", "main", "Capture an explicitly selected simulator display"),
    "open-safari": (
        "capture_macos",
        "open_safari",
        "Open a labeled calibration URL in a simulator",
    ),
    "safari-probe": (
        "run_safari_probe",
        "main",
        "Capture actual Safari native/PNG milestones with XCTest",
    ),
    "reference-session": (
        "reference_session",
        "main",
        "Operate native reference swipes and save Safari chrome frames",
    ),
    "reference-library": (
        "reference_library",
        "main",
        "Export or validate reviewed Device Hub canvas references",
    ),
    "png-compare": ("png_compare", "main", "Compare exact-size PNG regions without alignment"),
    "sample-appearance": (
        "sample_appearance",
        "main",
        "Sample encoded RGBA from explicit image regions",
    ),
    "contact-sheet": (
        "contact_sheet",
        "main",
        "Create a presentation sheet from explicit image pairs",
    ),
    "validate-mask": (
        "validate_framebuffer_mask",
        "main",
        "Compare framebuffer alpha with a local PDF mask",
    ),
    "validate-video": (
        "validate_video_evidence",
        "main",
        "Check decoded video frames and original source hash",
    ),
    "check-repository": (
        "check_repository",
        "main",
        "Audit public files without modifying Git state",
    ),
    "self-test": ("self_test", "main", "Run portable service, image, configuration and CLI checks"),
}


def main(argv=None):
    arguments = list(sys.argv[1:] if argv is None else argv)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=COMMANDS, help="Use COMMAND --help for its arguments")
    parser.epilog = "\n".join(f"{name}: {entry[2]}" for name, entry in COMMANDS.items())
    parser.formatter_class = argparse.RawDescriptionHelpFormatter
    if not arguments or arguments[0] in ("-h", "--help"):
        parser.print_help()
        return 0
    options = parser.parse_args(arguments[:1])
    module, function, _ = COMMANDS[options.command]
    original = sys.argv
    sys.argv = [f"devduo-tools {options.command}", *arguments[1:]]
    try:
        return getattr(importlib.import_module(f".{module}", __package__), function)() or 0
    except (OSError, ValueError, KeyError, SyntaxError, subprocess.SubprocessError) as error:
        print(f"devduo-tools {options.command}: {error}", file=sys.stderr)
        return 1
    finally:
        sys.argv = original
