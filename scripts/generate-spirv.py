#!/usr/bin/env python3
"""DEPRECATED — do not use. AstroCraft compiles shaders only via Vulkan SDK glslc.

Run:  powershell -File scripts/compile-shaders.ps1
Or:   dotnet build src/AstroCraft.Client   (runs compile-shaders.ps1 before build)
"""

import sys

print("ERROR: generate-spirv.py is deprecated. Use Vulkan SDK glslc via scripts/compile-shaders.ps1", file=sys.stderr)
sys.exit(1)
