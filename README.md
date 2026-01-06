# Neural Brain Interface

An interactive neural network application that provides real-time visualization of AI thinking processes.

## Project Structure

```
NeuralBrainInterface/
├── src/
│   ├── NeuralBrainInterface.Core/     # Core business logic and interfaces
│   └── NeuralBrainInterface.UI/       # WPF user interface
├── tests/
│   └── NeuralBrainInterface.Tests/    # Unit and property-based tests
└── NeuralBrainInterface.sln           # Solution file
```

## Features

- Real-time neural network visualization
- Multimodal input processing (text, image, video, audio, documents)
- Hardware device management (microphone, speaker, webcam)
- Memory management system (short-term and long-term)
- Brain file import/export functionality
- Sleep/wake state management
- Time and date awareness

## Technologies

- .NET 8.0
- WPF for UI
- xUnit for testing
- FsCheck for property-based testing
- NAudio for audio processing
- AForge.NET for video processing
- Various document processing libraries

## Getting Started

1. Open the solution in Visual Studio or your preferred IDE
2. Restore NuGet packages
3. Build the solution
4. Run the UI project

## Testing

Run tests using:
```bash
dotnet test
```