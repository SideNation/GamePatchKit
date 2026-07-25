@echo off

set BUILD_PROJECT_FILE=%~dp0_build\_build.csproj
set TEMP_DIRECTORY=%~dp0.nuke\temp

dotnet build "%BUILD_PROJECT_FILE%" /nodeReuse:false /p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet
dotnet run --project "%BUILD_PROJECT_FILE%" --no-build -- %*
