/**
** Copyright (c) 2015 - 2018 Tomas Kohl
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
**/

using System;
using AlacNetNAudioAdapter;
using NAudio.Wave;

namespace ALACDecoderDemo
{
    class Program
    {
        /// <summary>
        /// A simple command-line test utility - provide a path to an ALAC file and 
        /// it will try to play it and change a position within the stream
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            var path = args.Length > 0 ? args[0] : string.Empty;
            if (string.IsNullOrEmpty(path))
            {
                Console.WriteLine("Provide a path to an ALAC file as an argument");
                return;
            }
            if (!System.IO.File.Exists(path))
            {
                Console.WriteLine("Cannot open {0}, exiting.", path);
                return;
            }
            using (var stream = System.IO.File.OpenRead(path))
            {
                try
                {
                    using (var reader = new ALACFileReader(stream))
                    {
                        Console.WriteLine("ALAC file has {0} channels and {1} sample rate; total time: {2}",
                            reader.WaveFormat.Channels, reader.WaveFormat.SampleRate, reader.TotalTime);
                        var player = new WaveOutEvent();
                        player.PlaybackStopped += Player_PlaybackStopped;
                        player.Init(reader);
                        player.Play();
                        Console.WriteLine("Press any key to reposition to the middle of the stream...");
                        Console.ReadKey();
                        reader.Position = reader.Length / 2;
                        Console.WriteLine("Position: {0}, time: {1}", reader.Position, reader.CurrentTime);
                        Console.WriteLine("Press any key to stop playback...");
                        Console.ReadKey();
                    }
                }
                catch (System.IO.IOException e)
                {
                    Console.WriteLine("Problem reading ALAC file.");
                    Console.WriteLine(e.Message);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Problem playing back ALAC file.");
                    Console.WriteLine(e.Message);
                }
            }
        }

        private static void Player_PlaybackStopped(object sender, StoppedEventArgs e)
        {
            Console.WriteLine("Playback stopped.");
            if (e.Exception != null)
            {
                Console.WriteLine("Got an exception: {0}", e);
            }
            Console.WriteLine("Press any key to quit...");
            Console.ReadKey();
        }

    }
}
