# ALAC.NET

A decoder of Apple Lossless audio files for .NET with a NAudio adapter.

The bulk of the codebase is a port of a Java ALAC Decoder, Copyright 2011-2014 Peter McQuillan - see https://github.com/soiaf/Java-Apple-Lossless-decoder
I ported the code to C# without adding any new functionality except the NAudio WaveStream implementation, which you can use to read ALAC files in your app that uses NAudio.

The ALACDecoder library now targets Netstandard 2.0. Previously, it was a PCL library.

If you don't need NAudio support, simply ignore the ALACFileReader class and do what you wish with the ALACContext class that does the actual work.

Contributors welcome.
