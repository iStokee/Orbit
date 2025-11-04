# Orbit

> 🚀 **Modern WPF management interface for MemoryError** - Session control, script loading, and embedded RuneScape 3 clients

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-blue)](https://github.com/your-repo)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue)](https://github.com/your-repo)

## 📖 Overview

Orbit is a comprehensive management application for MemoryError that provides:
- **Session Management**: Launch, inject, and manage multiple RuneScape 3 client instances
- **Embedded Clients**: RuneScape 3 game windows embedded directly in tabs
- **Script Integration**: Load and manage C# scripts via MESharp hot reload system
- **Account Management**: Store and manage multiple account credentials
- **Theme System**: Full dark/light theme support with custom accent colors
- **Console Logging**: Unified logging from ME, scripts, and native subsystems

Orbit acts as a central hub that connects:
- **MemoryError (ME)**: Native C++ game hook injected into RS3
- **csharp_interop**: C# API for scripting
- **User Scripts**: External C# applications that can embed in Orbit tabs
- **RuneScape 3 Clients**: Multiple game instances running simultaneously

## 🚀 Quick Start

```bash
# 1. Launch Orbit
Orbit.exe

# 2. Create a new session
#    Click "Add Session" → Session launches with embedded RS3 client

# 3. Load a script (optional)
#    Navigate to Script Manager → Browse for .dll → Click "Load"

# 4. View logs
#    Navigate to Console tab → See live logs from all sources
```

For script integration, see [OrbitAPI](#-orbitapi-for-script-integration).

## ✨ Key Features

### 1. Session Management

Launch and manage multiple RuneScape 3 instances:

- **Create Sessions**: Launch new RS3 clients with auto-injection
- **Embedded Windows**: Game windows embedded directly in Orbit tabs
- **Process Monitoring**: Track client state, health, and injection status
- **Auto-Injection**: Optionally inject ME automatically when clients are ready
- **Session Persistence**: Remember sessions across app restarts

### 2. Script Integration

Load and manage C# scripts via the OrbitAPI:

```csharp
// From external script - register your window with Orbit
using Orbit;

var sessionId = OrbitAPI.RegisterScriptWindow(
    windowHandle,    // Your WPF window's HWND
    "My Script",     // Tab display name
    processId        // Optional
);

// Your window is now embedded as an Orbit tab!
```

**Features**:
- Scripts can embed their UI as tabs in Orbit
- Hot reload support via MemoryError's script loader
- Console output routed to Orbit's unified console
- Automatic cleanup when scripts unload

### 3. Unified Console

Multi-source console logging system:

- **Orbit System**: Logs from Orbit itself (launcher, injector, UI)
- **MemoryError (ME)**: Native C++ hook and subsystem logs
- **Scripts**: Logs from loaded C# scripts
- **Summary View**: Card-based overview of all log sources
- **Filtering**: View logs by source with dedicated tabs
- **Auto-Scroll**: Optional auto-scroll for live monitoring

### 4. Theme System

Comprehensive theming with MahApps.Metro:

- **Base Themes**: Dark and Light modes
- **Accent Colors**: 20+ built-in accent colors (Blue, Red, Green, Purple, etc.)
- **Custom Themes**: Import custom themes via JSON
- **Custom Accents**: Define custom accent colors (primary, secondary, highlight)
- **Theme Persistence**: Save theme preferences across sessions
- **Live Preview**: See theme changes instantly

### 5. Account Management

Secure credential storage:

- **Account Database**: Store multiple RS3 account credentials
- **MongoDB Integration**: Optional cloud sync for accounts
- **Quick Login**: One-click login to RS3 clients
- **Account Switching**: Change accounts without restarting
- **Security**: Encrypted storage (implementation TBD)

### 6. 📘 Orbiters Guide Hub

An in-app handbook styled like a well-loved field guide. It curates:

- 🚀 **Flight School**: Install, configure, and operate Orbit day-to-day
- 🛠️ **Contributor Manual**: Environment setup, coding standards, review flow
- 🧭 **API Reference**: Script APIs, Orbit services, plugin contracts
- 🧱 **Systems Primer**: Architectural overviews with links to deep dives
- 🗃️ **Quick Links**: Launch external docs and open the docs folder directly

The guide renders Markdown with Orbit's theme, includes quick status hints, and embraces the "Don't panic" Hitchhiker tone without sacrificing usability.

**How to Access**:
1. Click the **book icon** (📖) in Orbit's floating menu
2. Or go to Menu Settings → Enable/disable the "Guide" button

**Architecture**:
- Markdown-first renderer backed by [`Markdig`](https://github.com/xoofx/markdig) + [`Markdig.Wpf`](https://github.com/xoofx/markdig)
- Loads source content from `docs/OrbitersGuide` (packaged with Orbit builds)
- Auto-detects local repo clones to support live editing during development
- Status footer highlights load issues (missing files, parse errors)

**Use Cases**:
- Give operators a friendly "Don't panic" landing experience
- Onboard contributors with a single linkable handbook
- Surface API docs alongside architectural context and historical research
- Provide quick actions for refreshing or opening the docs folder for edits

### 7. 🔧 Extensible Tool System

Orbit features a **plugin-like tool architecture** for embedding reusable UI components:

**How It Works**:
```csharp
// Define a tool by implementing IOrbitTool
public class MyCustomTool : IOrbitTool
{
    public string Key => "MyTool";
    public string DisplayName => "My Custom Tool";
    public PackIconMaterialKind Icon => PackIconMaterialKind.Wrench;

    public FrameworkElement CreateView(object? context = null)
    {
        return new MyToolView(); // Your WPF UserControl
    }
}

// Register in App.xaml.cs
services.AddSingleton<IOrbitTool, MyCustomTool>();
```

**Built-in Tools**:
- **Script Controls**: Script loading and management
- **Settings**: Application preferences
- **Console**: Unified logging view
- **Theme Manager**: Theme and accent customization
- **Sessions Overview**: Session monitoring and control
- **Script Manager**: Script library and hot reload
- **Account Manager**: Account credentials management
- **Guide**: Orbiters Guide documentation hub

**Tool Registry**:
```csharp
public interface IToolRegistry
{
    IOrbitTool? Find(string key);
    IEnumerable<IOrbitTool> GetAll();
}
```

Tools are:
- **Discoverable**: Registered via DI and discovered at runtime
- **Reusable**: Can be opened multiple times or across sessions
- **Integrated**: Appear as tabs with consistent styling
- **Configurable**: Can be shown/hidden via Settings

**Adding Custom Tools**:
1. Create a class implementing `IOrbitTool`
2. Implement `CreateView()` to return your WPF UserControl
3. Register in `App.xaml.cs` with DI: `services.AddSingleton<IOrbitTool, YourTool>()`
4. Add command/method to `MainWindowViewModel` to open it
5. Add UI button (optional) in `MainWindow.xaml` floating menu

## 📁 Project Structure

```
Orbit/
├── MainWindow.xaml          # Main application shell
├── MainWindow.xaml.cs       # Main window logic
├── App.xaml                 # Application resources, theme initialization
├── App.xaml.cs              # Application startup and lifecycle
├── OrbitAPI.cs              # Public API for external script integration
├── Services/                # Core services
│   ├── SessionCollectionService.cs      # Session state management
│   ├── SessionManagerService.cs         # Session CRUD operations
│   ├── ScriptIntegrationService.cs      # External script embedding
│   ├── ScriptManagerService.cs          # Script loading via ME
│   ├── OrbitCommandClient.cs            # Communication with ME
│   ├── ThemeService.cs                  # Theme management
│   ├── AccountService.cs                # Account CRUD
│   └── AutoLoginService.cs              # Auto-login logic
├── ViewModels/              # MVVM view models
│   ├── MainWindowViewModel.cs           # Main window VM
│   ├── SessionsOverviewViewModel.cs     # Sessions tab VM
│   ├── ConsoleViewModel.cs              # Console logging VM
│   ├── ThemeManagerViewModel.cs         # Theme editor VM
│   ├── ScriptManagerViewModel.cs        # Script manager VM
│   └── AccountManagerViewModel.cs       # Account manager VM
├── Views/                   # User controls for tabs
│   ├── SessionsView.xaml                # Session management UI
│   ├── ConsoleView.xaml                 # Console log viewer
│   ├── ThemeManagerView.xaml            # Theme editor
│   ├── ScriptManagerView.xaml           # Script loader
│   ├── AccountManagerView.xaml          # Account management
│   └── ChildClientView.xaml             # Embedded window host
├── Models/                  # Data models
│   ├── SessionModel.cs                  # Session state
│   ├── AccountModel.cs                  # Account credentials
│   ├── ConsoleEntry.cs                  # Log entry
│   └── ThemeModel.cs                    # Theme definition
├── Tooling/                 # Tool system infrastructure
│   ├── IOrbitTool.cs                    # Tool interface
│   ├── ToolRegistry.cs                  # Tool discovery service
│   └── BuiltInTools/                    # Built-in tool implementations
│       ├── ScriptControlsTool.cs
│       ├── SettingsTool.cs
│       ├── ConsoleTool.cs
│       ├── ThemeManagerTool.cs
│       ├── SessionsOverviewTool.cs
│       ├── ScriptManagerTool.cs
│       ├── AccountManagerTool.cs
│       └── GuideTool.cs                 # Orbiters Guide wrapper
├── Classes/                 # Utilities
│   ├── Win32.cs                         # Win32 API interop
│   ├── SessionState.cs                  # Enums for session states
│   └── MELoader.cs                      # DLL injection helper
├── Converters/              # XAML value converters
├── Tooling/                 # Developer tools
└── Logging/                 # Logging infrastructure
```

## 🔨 Building

### Prerequisites
- .NET 8.0 SDK
- Visual Studio 2022 (recommended)
- `csharp_interop.dll` (for OrbitAPI integration)

### Build Commands

```bash
cd Orbit/Orbit

# Debug build
dotnet build Orbit.csproj -c Debug

# Release build
dotnet build Orbit.csproj -c Release

# Run
dotnet run -c Debug
```

### Output

- **Executable**: `Orbit.exe`
- **Platform**: x64 Windows
- **Framework**: .NET 8.0-windows

## Dependencies

```xml
<PackageReference Include="Costura.Fody" Version="6.0.0" />
<PackageReference Include="Dragablz" Version="0.0.3.234" />
<PackageReference Include="MahApps.Metro" Version="2.4.10" />
<PackageReference Include="MahApps.Metro.IconPacks" Version="5.1.0" />
<PackageReference Include="Microsoft.Xaml.Behaviors.Wpf" Version="1.1.135" />
<PackageReference Include="MongoDB.Driver" Version="3.0.0" />
<PackageReference Include="MvvmLightLibs" Version="5.4.1.1" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
<PackageReference Include="PixiEditor.ColorPicker" Version="3.4.2" />
```

**Key Libraries**:
- **Dragablz**: Tab reordering and docking
- **MahApps.Metro**: Modern WPF UI framework
- **MongoDB.Driver**: Account sync (optional)
- **Costura.Fody**: Embed dependencies into single executable

## 🏗️ Architecture

### MVVM Pattern

Orbit follows strict MVVM architecture:

- **Models**: Pure data classes (SessionModel, AccountModel, ConsoleEntry)
- **Views**: XAML UI (SessionsView, ConsoleView, ThemeManagerView)
- **ViewModels**: Business logic and data binding

### Dependency Injection

Uses built-in DI container pattern (not Microsoft.Extensions.DependencyInjection):

```csharp
// Manual DI in MainWindowViewModel
public MainWindowViewModel()
{
    // Create services
    SessionCollection = new SessionCollectionService();
    SessionManager = new SessionManagerService(SessionCollection);
    ScriptIntegration = new ScriptIntegrationService(SessionCollection);

    // Initialize OrbitAPI
    OrbitAPI.Initialize(ScriptIntegration);
}
```

### Service Layer

Core services that power Orbit:

| Service | Purpose |
|---------|---------|
| `SessionCollectionService` | Observable collection of active sessions |
| `SessionManagerService` | Create, update, delete sessions |
| `ScriptIntegrationService` | Embed external script windows |
| `ScriptManagerService` | Load scripts via ME |
| `OrbitCommandClient` | Named pipe communication with ME |
| `ThemeService` | Apply and save themes |
| `AccountService` | CRUD for account credentials |

## 💻 Using Orbit

### 1. Launching the Application

```bash
# Start Orbit
Orbit.exe
```

On first launch:
- Sets up default theme (Dark + Blue accent)
- Initializes logging system
- Discovers MemoryError installation

### 2. Creating a Session

**Via UI**:
1. Click "Add Session" button in Sessions tab
2. Configure session options (auto-inject, etc.)
3. Session launches with RS3 client embedded in tab

**Session States**:
- **NotStarted**: Session created but client not launched
- **ClientRunning**: RS3 client process running
- **ClientReady**: Client ready for injection
- **InjectionReady**: ME injected successfully
- **Error**: Something went wrong

### 3. Loading Scripts

**Via Script Manager Tab**:
1. Navigate to "Script Manager" tab
2. Browse for compiled script DLL
3. Click "Load Script"
4. Script initializes via hot reload system

**Via ME Console**:
Scripts can also be loaded directly through MemoryError's ImGui interface.

### 4. Managing Accounts

**Via Account Manager Tab**:
1. Navigate to "Account Manager"
2. Add account credentials
3. Assign accounts to sessions
4. Use "Quick Login" to auto-fill credentials

### 5. Viewing Logs

**Console Tab**:
- **Summary**: Card-based overview of all log sources
- **All Sources**: Unified view of all logs
- **Orbit**: Orbit system logs only
- **MemoryError**: ME native logs only
- **Scripts**: Script output only

## 🔌 OrbitAPI for Script Integration

External scripts can integrate with Orbit via the `OrbitAPI` class.

### Checking if Orbit is Available

```csharp
if (OrbitAPI.IsOrbitAvailable())
{
    // Orbit is running, we can integrate
}
```

### Registering a Script Window

```csharp
using System;
using System.Windows;
using System.Windows.Interop;
using Orbit;

// Get your WPF window's Win32 handle
var windowHandle = new WindowInteropHelper(myWpfWindow).Handle;

// Register with Orbit
Guid sessionId = OrbitAPI.RegisterScriptWindow(
    windowHandle,
    "My Script Name",
    Process.GetCurrentProcess().Id
);

// Your window is now embedded in an Orbit tab!
```

### Unregistering on Shutdown

```csharp
// Clean up when your script shuts down
OrbitAPI.UnregisterScriptWindow(sessionId);
```

### Full Integration Example

From the WPF Debug Utility:

```csharp
public static void Initialize()
{
    // Create WPF window
    var window = new MainWindow();
    window.Show();

    // Try to integrate with Orbit if available
    var windowHandle = new WindowInteropHelper(window).Handle;

    Type? orbitApiType = Type.GetType("Orbit.OrbitAPI, Orbit");
    if (orbitApiType != null)
    {
        var isAvailableMethod = orbitApiType.GetMethod("IsOrbitAvailable");
        var isAvailable = (bool)isAvailableMethod?.Invoke(null, null);

        if (isAvailable)
        {
            var registerMethod = orbitApiType.GetMethod("RegisterScriptWindow");
            var sessionId = registerMethod?.Invoke(null, new object[] {
                windowHandle,
                "MESharp Debug",
                null
            });

            Console.WriteLine($"[Orbit] Registered with session ID: {sessionId}");
        }
    }
}
```

## 📡 Communication with MemoryError

Orbit communicates with ME via multiple channels:

### 1. Named Pipe (OrbitCommandClient)

Sends commands to ME:

```csharp
// Example: Load a script
OrbitCommandClient.SendCommand($"LOAD\t{scriptPath}");

// Example: Reload a script
OrbitCommandClient.SendCommand($"RELOAD\t{scriptPath}");
```

**Supported Commands**:
- `LOAD\t<path>`: Load a .NET script
- `RELOAD\t<path>`: Reload an existing script
- `UNLOAD\t<name>`: Unload a script

### 2. DLL Injection (MELoader)

Injects `XInput1_4_inject.dll` into RS3 clients:

```csharp
// From SessionManagerService
MELoader.InjectIntoProcess(rsProcess, meDllPath);
```

### 3. Console Log Streaming

ME streams logs back to Orbit via shared memory or named pipe (implementation varies).

## 🔄 Session Lifecycle

```
[User Creates Session]
        ↓
[Launch RS3 Client Process]
        ↓
[Wait for Client Window Ready]
        ↓
[Inject ME DLL (XInput1_4_inject.dll)]
        ↓
[ME Initializes .NET Runtime]
        ↓
[ME Hooks Game Functions]
        ↓
[Session Ready for Scripts]
        ↓
[User Loads Scripts]
        ↓
[Scripts Execute via MESharp API]
```

## 🎨 Theme System

Orbit's theme system is built on MahApps.Metro:

### Built-in Themes

**Base Themes**:
- Light
- Dark

**Accent Colors**:
- Red, Green, Blue, Purple, Orange, Lime, Emerald, Teal, Cyan, Cobalt, Indigo, Violet, Pink, Magenta, Crimson, Amber, Yellow, Brown, Olive, Steel, Mauve, Taupe, Sienna

### Custom Themes

Create custom themes via JSON:

```json
{
  "Name": "MyCustomTheme",
  "BaseTheme": "Dark",
  "PrimaryAccent": "#00D9FF",
  "SecondaryAccent": "#0099CC",
  "HighlightAccent": "#00FFFF"
}
```

Import via Theme Manager UI or place in `CustomAccents/` folder.

### Applying Themes

**Via UI**:
1. Navigate to "Theme Manager" tab
2. Select base theme (Dark/Light)
3. Select accent color
4. Click "Apply"

**Programmatically**:
```csharp
ThemeService.ApplyTheme("Dark", "Blue");
```

## 📝 Logging System

Orbit features a comprehensive logging system:

### Log Sources

Each log entry has a source:

```csharp
public enum LogSource
{
    Orbit,         // Orbit system logs
    MemoryError,   // Native ME logs
    Scripts,       // User script logs
    External       // Other external sources
}
```

### Log Levels

```csharp
public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}
```

### Creating Log Entries

From Orbit code:

```csharp
ConsoleService.Log(LogLevel.Info, LogSource.Orbit, "Session created successfully");
```

From scripts (via MESharp):

```csharp
Console.WriteLine("This will appear in Orbit's Scripts tab");
```

### Console View Features

- **Live Filtering**: Filter by source and level
- **Search**: Full-text search across all logs
- **Auto-Scroll**: Toggle auto-scroll for live monitoring
- **Export**: Export logs to file (future feature)
- **Colorization**: Color-coded by log level

## Advanced Features

### Floating Action Menu

Orbit includes an optional floating action menu that appears over RS3 game windows:

- **Appearance**: Customizable opacity, position, direction
- **Actions**: Quick access to inject, scripts, console
- **Auto-Hide**: Hides after inactivity
- **Positioning**: Docks to screen edges

Configure via Settings tab.

### Inter-Tab Communication

Tabs can communicate via the MainWindowViewModel:

```csharp
// From ScriptManagerViewModel
MainWindowViewModel.SwitchToTab("Console");
MainWindowViewModel.ConsoleViewModel.AddEntry(logEntry);
```

### Window Embedding

External windows are embedded using Win32 interop:

```csharp
// From ChildClientView
Win32.SetParent(childHwnd, hostHwnd);
Win32.SetWindowLong(childHwnd, GWL_STYLE, WS_CHILD | WS_VISIBLE);
```

## Development Workflow

### Adding a New Tool Tab

1. **Create View**: `Views/NewToolView.xaml`
2. **Create ViewModel**: `ViewModels/NewToolViewModel.cs`
3. **Register in MainWindow**:
   ```xaml
   <TabItem Header="New Tool">
       <views:NewToolView DataContext="{Binding NewToolViewModel}" />
   </TabItem>
   ```
4. **Wire ViewModel**:
   ```csharp
   public NewToolViewModel NewToolViewModel { get; }

   public MainWindowViewModel()
   {
       NewToolViewModel = new NewToolViewModel();
   }
   ```

### Adding a New Service

1. **Create Service**: `Services/NewService.cs`
2. **Inject Dependencies**: Pass required services via constructor
3. **Register in MainWindowViewModel**:
   ```csharp
   public NewService NewService { get; }

   public MainWindowViewModel()
   {
       NewService = new NewService(dependency1, dependency2);
   }
   ```

### Release Automation

- Run `Orbit/tools/OrbitRelease.ps1` to bump versions, publish binaries, and build Velopack packages.
- Option **6** executes the full pipeline and regenerates `orbit-win-x64.zip` for the built-in updater.
- GitHub Actions workflow `.github/workflows/orbit-release.yml` mirrors the script when tags such as `orbit/v1.2.3` are pushed or via manual dispatch.

## Relationship with csharp_interop

Orbit **references** `csharp_interop.dll` for:

1. **OrbitAPI Integration**: Scripts using OrbitAPI link against csharp_interop
2. **Type Definitions**: Shared types between Orbit and scripts

**Reference Setup**:
```xml
<ItemGroup>
  <Reference Include="csharp_interop">
    <HintPath>..\..\C#\csharp_interop\bin\Debug\net8.0-windows\csharp_interop.dll</HintPath>
  </Reference>
</ItemGroup>
```

Orbit does **not** provide scripting functionality itself - that comes from MemoryError + csharp_interop.

## Troubleshooting

| Issue | Solution |
|-------|----------|
| **Session won't launch** | Check RS3 install path, verify ME DLL exists |
| **Injection fails** | Run Orbit as Administrator, check antivirus |
| **Embedded window blank** | Verify RS3 client is windowed mode, not fullscreen |
| **Script window not embedding** | Ensure OrbitAPI.RegisterScriptWindow was called correctly |
| **Theme not applying** | Clear theme cache, restart Orbit |
| **Logs not appearing** | Check console tab is visible, verify log source filter |

## Platform

- **Target**: `.NET 8.0-windows8.0`
- **UI Framework**: WPF
- **Assembly Name**: `Orbit`
- **Platform**: `x64` only
