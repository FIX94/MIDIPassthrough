# MIDIPassthrough
This is a small C# project written for windows vista or newer where it is not possible to select a default MIDI device anymore with consistency.
To compile this project you will have to download and install the VirtualMIDI SDK and also copy teVirtualMIDI.cs from its Cs-Binding folder into this project folder.
The application will directly go into your system tray which is handy for having it autostart with windows, clicking on the tray icon will bring up its GUI.
Starting the application will overwrite the current MIDI driver in the windows registry with VirtualMIDI until you close the application again. 
This means all the default MIDI I/O will be sent directly to this application.
While the application is open you can decide to which MIDI devices you want to for both input and output.
After you close the application your device selections will be saved for the next time.

This project contains files from MIDI.NET:  
https://midinet.codeplex.com/