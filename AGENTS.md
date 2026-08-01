# AGENTS.md

## Project Overview
- **Type:** WPF (.NET 8) / Node.js ES Modules
- **Architecture:** MVVM with CommunityToolkit.Mvvm / Express API

## Build & Test Commands
- **Run Tests:** `dotnet test` or `npm test`
- **Build Solution:** `dotnet build` or `npm run build`

## Key Boundaries
- Do not edit third-party wrappers in `src/Vendor/`.
- Put application-wide XAML Styles in `App.xaml`. Put control-specific default styles in `Themes/Generic.xaml`.