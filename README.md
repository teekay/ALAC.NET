# ALAC.NET

A decoder of Apple Lossless audio files for .NET with a NAudio adapter.

The bulk of the codebase is a port of a Java ALAC Decoder, Copyright 2011-2014 Peter McQuillan - see https://github.com/soiaf/Java-Apple-Lossless-decoder
I ported the code to C# without adding any new functionality except the NAudio WaveStream implementation, which you can use to read ALAC files in your app that uses NAudio.

The library is configured to be portable, meaning you should in theory be able to use in for projects in Windows Store and Windows Phone apps as well as traditional .NET desktop apps.

If you don't need NAudio support, simply ignore the ALACFileReader class and do what you wish with the ALACContext class that does the actual work.

Contributors welcome.
