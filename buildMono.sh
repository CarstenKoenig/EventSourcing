#!/bin/bash

mono --runtime=v4.0 tools/NuGet/nuget.exe install FAKE -OutputDirectory tools -ExcludeVersion
mono --runtime=v4.0 tools/FAKE/tools/FAKE.exe buildMono.fsx $@