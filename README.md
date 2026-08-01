# ApexAstro

A modern WPF application for browsing and reviewing astronomical images and FITS files. Designed specifically for astrophotographers who need to quickly review image quality and manage their subexposures.

<img width="3701" height="2007" alt="AstroImages" src="https://github.com/user-attachments/assets/fceba398-5514-4f59-a656-16fe1f0d632f" />

![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)
![Version](https://img.shields.io/badge/Version-1.9.0-blue.svg)
![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-brightgreen.svg)

## Key Features

- **View FITS and XISF astronomical images** - Includes display of metadata for these file formats
- **View other common image formats** - JPG, PNG, BMP, GIF, WEBP and TIFF
- **Zoom and pan controls** - Navigate through your images with mouse wheel zoom and drag-to-pan functionality
- **File metadata display** - View filename metadata as well as info in FITS/XISF headers
- **Select and move images** - Select images and then move them to a processing folder or the recycle bin
- **Image statistics and analysis** - Examine pixel statistics and image properties including median values (normalized or 16-bit)
- **Histogram display** - Optional histogram panel with linear and logarithmic scales
- **Floating windows** - Detach image and histogram viewers to separate windows for multi-monitor setups
- **Dark mode** - Switch between light and dark themes for comfortable viewing
- **Median & Mean** - Calculates these values and displays then as 0.0-1.0 or 0-65535
- **Session Metadata NINA plugin values** - Reads the AquisitionDetails.csv and ImageMetaData.csv if present

---

## Project Status

ApexAstro is actively maintained and ready for production use. The application includes 146+ unit tests covering core functionality (FITS/XISF parsing, file management, metadata extraction). See [RELEASE_NOTES.txt](RELEASE_NOTES.txt) for the latest changes.

For security concerns, please refer to [SECURITY.md](SECURITY.md).

---

## Support for NINA Users

If you use NINA (N.I.N.A. - Nighttime Imaging 'N' Astronomy) some useful data is not stored in the FITS header but can be added to the image file name with filenames like this in Options -> Imaging:
```
$$TARGETNAME$$\NIGHT_$$DATEMINUS12$$\$$IMAGETYPE$$\$$DATETIME$$_$$FILTER$$_RMS:$$RMSARCSEC$$_HFR:$$HFR$$_ECC_$$ECCENTRICITY$$_FWHM_$$FWHM$$_Stars_$$STARCOUNT$$_$$GAIN$$_$$EXPOSURETIME$$s_$$SENSORTEMP$$C_$$FRAMENR$$
```
That generates filenames such as:
```
M33 › NIGHT_2015-12-31 › LIGHT › 2016-01-01_12-00-00_L_RMS_0.35_HFR_3.25_ECC_0.66_FWHM_0.00_Stars_3294_1600_10.21s_-15C_0001
```

This app can parse RMS, HFR, ECCENTRICITY, FWHM, Stars, and other quality metrics directly from the filename for quick quality assessment. Custom keyword values are displayed in green in the file list. The Image Type, Filter, Exposure Time, Sensor Temperature, and other metadata are stored in the FITS file and displayed in blue when added as columns. The median value (shown in purple) can be displayed as normalized (0.0-1.0) or 16-bit range (0-65535) format.

---

## Installation

### System Requirements
- **Windows 10/11** (64-bit)
- **.NET 8 Desktop Runtime** - [Download here](https://dotnet.microsoft.com/download/dotnet/8.0) (choose "Desktop Runtime")

### Installation

#### Option 1: Installer (Recommended)
1. **Download** `ApexAstro-Setup-x.x.x.exe` from [GitHub Releases](https://github.com/kfaubel/ApexAstro/releases)
2. **Run** the installer
3. The installer will:
   - Check for .NET 8 Desktop Runtime (offers to download if missing)
   - Install to `%LOCALAPPDATA%\ApexAstro`
   - Create Start Menu shortcuts
   - Use streamlined installation (only 2-3 clicks required)

#### Option 2: Portable ZIP
1. **Download** `ApexAstro-win-x64.zip` from [GitHub Releases](https://github.com/kfaubel/ApexAstro/releases)
2. **Extract** all files to a folder of your choice
3. **Run** `ApexAstro.exe`

#### Advanced: Silent/Automated Installation

The installer supports command-line flags for automated deployment:

```powershell
# Silent installation (shows progress bar)
ApexAstro-Setup-v1.5.0.exe /SILENT

# Very silent installation (no UI at all)
ApexAstro-Setup-v1.5.0.exe /VERYSILENT

# Silent with custom directory
ApexAstro-Setup-v1.5.0.exe /VERYSILENT /DIR="C:\MyApps\ApexAstro"

# Complete silent installation with logging
ApexAstro-Setup-v1.5.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="install.log"
```

**Common flags:**
- `/SILENT` - Silent mode with progress window
- `/VERYSILENT` - Completely silent (no UI)
- `/SUPPRESSMSGBOXES` - Suppress message boxes during installation
- `/NORESTART` - Prevent automatic restart
- `/DIR="path"` - Specify installation directory
- `/LOG="file"` - Create installation log file

*First-time users: If you don't have .NET 8 Desktop Runtime, Windows will prompt you to install it automatically.*

---

## Development Setup

### Prerequisites

- **Visual Studio Code** (free, cross-platform editor)
  - Extensions: "C# Dev Kit" (includes C# extension and .NET debugging)
- **.NET 8 SDK** (download from Microsoft)
- **Windows 10/11** (WPF is Windows-only)
- **Git** for version control

### Quick Start

1. **Clone the repository**
   ```powershell
   git clone https://github.com/kfaubel/ApexAstro.git
   cd ApexAstro
   ```

2. **Open in VS Code**
   - Open the folder in VS Code: `code .`
   - Install recommended extensions when prompted:
     - **C# Dev Kit** (includes C#, C# IntelliCode, and .NET Install Tool)
     - **XAML** (for XAML syntax highlighting and IntelliSense)
   - VS Code will automatically restore NuGet packages when you open a .csproj file
   - VS Code will automatically generate launch configurations in `.vscode/launch.json` and tasks in `.vscode/tasks.json`.

3. **Build and run**
   - Press `F5` to build and run with debugger (or use Command Palette: "Debug: Start Debugging")
   - Or press `Ctrl+F5` to run without debugger
   - Alternatively, use the terminal: `dotnet run --project ApexAstro.Wpf`

### Command Line Development

```powershell
# Restore dependencies
dotnet restore

# Remove derived files
dotnet clean

# Build the solution
dotnet build

# Run the application
dotnet run --project ApexAstro.Wpf

# Build specific configuration
dotnet build --configuration Release

# Watch for changes and auto-rebuild
dotnet watch --project ApexAstro.Wpf
```
---

## Development Guide

### Project Structure

```
ApexAstro/
├── ApexAstro.Wpf/          # Main WPF application
│   ├── Views/                # XAML windows and user controls
│   ├── ViewModels/           # MVVM view models
│   ├── Services/             # Business logic services
│   ├── Models/               # Data models
│   └── App.xaml              # Application entry point
├── ApexAstro.Core/         # Core library (FITS parsing, etc.)
├── ApexAstro.Utils/        # Utility classes
└── TestData/                 # Sample FITS files for testing
```

---

### Creating Distributable Releases

#### Recommended: Framework-Dependent Deployment

```powershell
# Small, fast download - requires .NET 8 Desktop Runtime
dotnet publish ApexAstro.Wpf -c Release -r win-x64 --no-self-contained
```

### Publishing to GitHub Releases

See the [.github/workflows/release.yml](.github/workflows/release.yml) for the automated release workflow.

The workflow automatically creates both a ZIP file and an installer when you push a version tag.

#### Release Checklist

Follow these steps to publish a new version:

1. **Update Version Numbers** (use the same version in all locations):
   - [ ] `ApexAstro.Wpf/ApexAstro.Wpf.csproj` - Update `<Version>` property (e.g., `1.5.0`)
   - [ ] `installer.iss` - Update `#define MyAppVersion` (e.g., `"1.5.0"`)

2. **Update Release Notes**:
   - [ ] Edit `RELEASE_NOTES.txt` with new features, improvements, and bug fixes
   - [ ] Update version number in the first line of `RELEASE_NOTES.txt`
   - [ ] This file is displayed in both the installer and the "What's New" section of the update dialog

3. **Test Locally** (optional but recommended):
   ```powershell
   # Build and test the application
   dotnet build -c Release
   dotnet run --project ApexAstro.Wpf -c Release
   
   # Test the installer build (requires Inno Setup installed)
   dotnet publish ApexAstro.Wpf -c Release -r win-x64 --no-self-contained --output ./publish
   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
   ```

4. **Commit Changes**:
   ```powershell
   git add .
   git commit -m "Release v1.5.0"
   ```

5. **Create and Push Tag**:
   ```powershell
   # Create a version tag (must start with 'v')
   git tag v1.5.0
   
   # Push commits and tag to trigger GitHub Actions
   git push origin main
   git push origin v1.5.0
   
   # Or push everything at once:
   git push origin main --tags
   ```

6. **Monitor GitHub Actions**:
   - [ ] Go to https://github.com/kfaubel/ApexAstro/actions
   - [ ] Wait for the build to complete (~5-10 minutes)
   - [ ] Check for any errors in the workflow

7. **Verify Release**:
   - [ ] Go to https://github.com/kfaubel/ApexAstro/releases
   - [ ] Verify both `ApexAstro-win-x64.zip` and `ApexAstro-Setup-x.x.x.exe` are present
   - [ ] Download and test the installer
   - [ ] Check that release notes are displayed correctly

**Quick Release Command** (after completing steps 1-2):
```powershell
git add . && git commit -m "Release v1.5.0" && git tag v1.5.0 && git push origin main --tags
```

---

## Release Scripts

Two convenience scripts automate the most tedious parts of a release: bumping version numbers in the `.csproj`, committing, tagging, and pushing to GitHub.

### `release.cmd` — Quick patch release

The simplest way to ship a patch:

```cmd
release.cmd
```

This calls `release.ps1 patch` for you (bypassing any PowerShell execution-policy restrictions).  
It bumps the **patch** component (e.g. `1.5.2` → `1.5.3`), commits, tags, and pushes.

### `release.ps1` — Full control

```powershell
# Bump the patch version (default, same as release.cmd)
.\release.ps1

# Bump the minor version  (e.g. 1.5.2 → 1.6.0)
.\release.ps1 minor

# Bump the major version  (e.g. 1.5.2 → 2.0.0)
.\release.ps1 major
```

**What the script does, step by step:**

1. Reads the current `<Version>` from `ApexAstro.Wpf/ApexAstro.Wpf.csproj`.
2. Calculates the new version according to the chosen bump type.
3. Asks for confirmation before making any changes.
4. Updates `<Version>`, `<AssemblyVersion>`, and `<FileVersion>` in the `.csproj`.
5. Runs `git add`, `git commit`, `git tag`, and `git push` for both the commit and the new tag.
6. Prints the GitHub Actions URL so you can watch the build.

**Before running the script, make sure you have:**

- [ ] Edited `RELEASE_NOTES.txt` with what changed in this version.
- [ ] All code changes committed (the script only touches the `.csproj` version fields).

**After the script completes:**

- GitHub Actions picks up the new `vX.Y.Z` tag and builds the installer and ZIP automatically (~5–10 minutes).
- Check progress at <https://github.com/kfaubel/ApexAstro/actions>.
- The finished assets appear at <https://github.com/kfaubel/ApexAstro/releases>.

### Building the Installer Locally (Testing)

To test the installer before pushing a release:

1. **Install Inno Setup** (if not already installed):

   **Option A: Direct Download** (Recommended)
   - Go to [jrsoftware.org/isdl.php](https://jrsoftware.org/isdl.php)
   - Download "Inno Setup 6.x.x" (first download link)
   - Run the installer
   - Follow the installation wizard
   
   **Option B: Using Chocolatey** (if you have Chocolatey installed)
   ```powershell
   choco install innosetup -y
   ```
   
   **Option C: Install Chocolatey first, then Inno Setup**
   ```powershell
   # Run PowerShell as Administrator, then:
   Set-ExecutionPolicy Bypass -Scope Process -Force
   [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
   iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))
   
   # Then install Inno Setup:
   choco install innosetup -y
   ```

2. **Build and create installer**:
   ```powershell
   # Build the application
   dotnet publish ApexAstro.Wpf -c Release -r win-x64 --no-self-contained --output ./publish
   
   # Compile the installer (command line)
   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
   
   # Or open installer.iss in Inno Setup GUI and click Build -> Compile
   ```

3. **Test the installer**: Find it at `installer-output/ApexAstro-Setup-x.x.x.exe`

---

## Code Signing (Future)

### Why Sign Your Application?

- **Windows SmartScreen**: Unsigned apps trigger warnings
- **User trust**: Digital signatures verify authenticity
- **Professional distribution**: Required for some deployment scenarios

### Getting a Code Signing Certificate

#### Option 1: Commercial Certificate (Recommended)
- **Sectigo** (formerly Comodo): ~$200/year
- **DigiCert**: ~$400/year
- **GlobalSign**: ~$300/year

#### Option 2: Self-Signed Certificate (Testing Only)
```powershell
# Create self-signed certificate (development only)
New-SelfSignedCertificate -DnsName "YourCompany" -Type CodeSigning -CertStoreLocation cert:\CurrentUser\My
```

### Signing Your Application

Maybe: https://learn.microsoft.com/en-gb/windows/msix/

#### Using SignTool (Windows SDK)

```powershell
# Install Windows SDK or Visual Studio (includes SignTool)

# Sign the executable
signtool sign /f "certificate.pfx" /p "password" /fd SHA256 /tr http://timestamp.sectigo.com /td SHA256 "ApexAstro.Wpf.exe"

# Verify signature
signtool verify /pa "ApexAstro.Wpf.exe"
```

#### Using MSBuild (Automated)

Add to your `.csproj` file:

```xml
<PropertyGroup Condition="'$(Configuration)'=='Release'">
  <SignAssembly>true</SignAssembly>
  <AssemblyOriginatorKeyFile>certificate.pfx</AssemblyOriginatorKeyFile>
  <SignToolPath>C:\Program Files (x86)\Windows Kits\10\bin\10.0.22000.0\x64\signtool.exe</SignToolPath>
</PropertyGroup>

<Target Name="SignOutput" AfterTargets="Publish">
  <Exec Command="&quot;$(SignToolPath)&quot; sign /f certificate.pfx /p $(CertPassword) /fd SHA256 &quot;$(PublishDir)ApexAstro.Wpf.exe&quot;" />
</Target>
```

> **Note**: You can edit `.csproj` files directly in VS Code with XML syntax highlighting and IntelliSense support.

#### Timestamping

Always include timestamping when signing:
- **Sectigo**: `http://timestamp.sectigo.com`
- **DigiCert**: `http://timestamp.digicert.com`
- **GlobalSign**: `http://timestamp.globalsign.com/tsa/r6advanced1`

---

## User Guide

See the ApexAstro.Wpf/Documentation/Help.md file or run the app and go to Help -> Documentation

### Keyboard Shortcuts

- **Ctrl+O**: Open Folder
- **Ctrl+M**: Move Selected Files
- **Space**: Play/Pause slideshow
- **↑/↓ Arrow Keys**: Navigate between images
- **+/-**: Zoom in/out
- **Ctrl+0**: Fit image to window
- **Ctrl+1**: Actual size (1:1)
- **F1**: Show help documentation

#

## License

MIT License - see LICENSE file for details.

---

## Attribution

### Third-Party Components

This application uses the following open-source libraries:

- **[MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet)** (v2.8.1) by Drew Noakes - Apache License 2.0
  - Used for extracting EXIF and other metadata from image files

### Development Tools

This application was developed with significant assistance from **GitHub Copilot**, demonstrating the collaborative potential between human creativity and AI-powered development tools.  <-- Said the AI bot 😀

