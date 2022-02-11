using System;
using System.IO;

class LosslessAudioCodec
{
    static string inputFilePath;
    static string outputFilePath;
    static FileStream inputFileStream;
    static FileStream outputFileStream;
    static BinaryReader input;
    static BinaryWriter output;
    static uint inputDataSize;
    static uint outputDataSize;

    static void Main(string[] args)
    {
        PrintAbout();

        if (args.Length != 1)
        {
            PrintHelp();
            return;
        }

        try
        {
            if (!Path.HasExtension(args[0]))
            {
                Console.WriteLine("Error: File extension is not specified!");
                return;
            }
        }
        catch (ArgumentException)
        {
            Console.WriteLine("Error: Invalid command-line argument!");
            return;
        }

        string fileExtension = Path.GetExtension(args[0]).ToLower();

        if (fileExtension != ".wav" && fileExtension != ".plac")
        {
            Console.WriteLine("Error: Invalid file extension!");
            return;
        }

        if (!File.Exists(args[0]))
        {
            Console.WriteLine("Error: File \"{0}\" not found!", args[0]);
            return;
        }

        inputFilePath = args[0];

        if (fileExtension == ".wav")
        {
            Encode();
        }
        else
        {
            Decode();
        }
    }

    static void PrintAbout()
    {
        Console.WriteLine("PLAC - Plamen's Lossless Audio Codec   Version 0.1");
        Console.WriteLine("Copyright (c) 2022 Plamen Iliev. All rights reserved.\n");
    }

    static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("       encode -> plac filename.wav");
        Console.WriteLine("       decode -> plac filename.plac");
        Console.WriteLine("\nFor now PLAC supports only 8-bit 8000Hz mono PCM audio.");
    }

    static void PrintErrorAndExit(string errorMessage, string filePath = "")
    {
        Console.WriteLine(errorMessage, filePath);

        if (input != null)
        {
            input.Dispose();
        }

        if (output != null)
        {
            output.Dispose();

            try
            {
                File.Delete(outputFilePath);
            }
            catch (SystemException)
            {
            }
        }

        Environment.Exit(1);
    }

    static void Encode()
    {
        int startTime = Environment.TickCount;

        OpenInputFile();
        ReadWavHeader();
        CreateOutputFile(".plac");
        WritePlacHeader();

        Console.Write("Encoding file ...");

        uint numberOfFrames = inputDataSize / 32;
        uint lastFrameSize = inputDataSize % 32;

        outputDataSize = 0;

        try
        {
            for (uint frame = 0; frame < numberOfFrames; frame++)
            {
                byte[] inData = input.ReadBytes(32);
                byte maxValue = 0;

                // This little code is essential. It performs binary offset
                // to sign-magnitude to zig-zag encoding and finds the max value
                for (int sample = 0; sample < 32; sample++)
                {
                    if (inData[sample] > 127)
                    {
                        inData[sample] <<= 1;
                    }
                    else
                    {
                        inData[sample] = (byte)((~inData[sample] << 1) + 1);
                    }

                    if (inData[sample] > maxValue)
                    {
                        maxValue = inData[sample];
                    }
                }

                output.Write(maxValue);
                outputDataSize++;

                // Get the number of bits of max value. It's slow! Will fix in Version 0.2
                int bits = (int)Math.Ceiling(Math.Log(maxValue + 1) / Math.Log(2));

                // Here the magic happens
                for (int firstSample = 0; firstSample < 32; firstSample += 8)
                {
                    byte[] outData = new byte[bits];
                    ulong buffer = 0ul;

                    // Load the buffer
                    for (int sample = firstSample; sample < firstSample + 8; sample++)
                    {
                        buffer <<= bits;
                        buffer |= inData[sample];
                    }

                    // Dump the buffer
                    for (int i = bits - 1; i >= 0; i--)
                    {
                        outData[i] = (byte)buffer;
                        buffer >>= 8;
                    }

                    output.Write(outData);
                    outputDataSize += (uint)bits;
                }
            }

            // Write the last frame
            output.Write(input.ReadBytes((int)lastFrameSize));
            outputDataSize += lastFrameSize;

            // Fill the blank fields
            output.Seek(8, SeekOrigin.Begin);
            output.Write(inputDataSize);
            output.Write(outputDataSize);
        }
        catch (IOException)
        {
            PrintErrorAndExit("\rError reading/writing files!");
        }

        // Close all files
        input.Dispose();
        output.Dispose();

        int stopTime = Environment.TickCount;
        int encodingTime = stopTime - startTime;

        // Print stats
        Console.WriteLine("\rEncoding done!   Time: {0:f2}sec   Ratio: {1:f3}", encodingTime / 1000.0, (double)outputDataSize / inputDataSize);
    }

    static void Decode()
    {
        int startTime = Environment.TickCount;

        OpenInputFile();
        ReadPlacHeader();
        CreateOutputFile(".wav");
        WriteWavHeader();

        Console.Write("Decoding file ...");

        uint numberOfFrames = outputDataSize / 32;
        uint lastFrameSize = outputDataSize % 32;

        inputDataSize = 0;

        try
        {
            for (uint frame = 0; frame < numberOfFrames; frame++)
            {
                byte[] outData = new byte[32];
                byte maxValue = input.ReadByte();

                inputDataSize++;

                int bits = (int)Math.Ceiling(Math.Log(maxValue + 1) / Math.Log(2));

                for (int firstSample = 0; firstSample < 32; firstSample += 8)
                {
                    byte[] inData = input.ReadBytes(bits);
                    ulong buffer = 0ul;
                    ulong mask = (1ul << bits) - 1;

                    inputDataSize += (uint)bits;

                    // Load the buffer
                    for (int i = 0; i < bits; i++)
                    {
                        buffer <<= 8;
                        buffer |= inData[i];
                    }

                    // Dump the buffer
                    for (int sample = firstSample + 7; sample >= firstSample; sample--)
                    {
                        outData[sample] = (byte)(buffer & mask);
                        buffer >>= bits;
                    }
                }

                // This code converts samples back to offset binary (excess-128)
                for (int sample = 0; sample < 32; sample++)
                {
                    if ((outData[sample] & 1) == 0)
                    {
                        outData[sample] = (byte)((outData[sample] >> 1) + 128);
                    }
                    else
                    {
                        outData[sample] = (byte)((byte)~outData[sample] >> 1);
                    }
                }

                output.Write(outData);
            }

            // Write the last frame
            output.Write(input.ReadBytes((int)lastFrameSize));
            inputDataSize += lastFrameSize;

            // Add a padding byte if necessary
            if (outputDataSize % 2 == 1)
            {
                output.Write((byte)0);
            }

            // Fill the blank fields
            output.Seek(4, SeekOrigin.Begin);
            output.Write((uint)(outputFileStream.Length - 8));
            output.Seek(40, SeekOrigin.Begin);
            output.Write(outputDataSize); ;
        }
        catch (IOException)
        {
            PrintErrorAndExit("\rError reading/writing files!");
        }

        // Close all files
        input.Dispose();
        output.Dispose();

        int stopTime = Environment.TickCount;
        int decodingTime = stopTime - startTime;

        // Print stats
        Console.WriteLine("\rDecoding done!   Time: {0:f2}sec   Ratio: {1:f3}", decodingTime / 1000.0, (double)outputDataSize / inputDataSize);
    }

    static void OpenInputFile()
    {
        try
        {
            inputFileStream = new FileStream(inputFilePath, FileMode.Open, FileAccess.Read);
            input = new BinaryReader(inputFileStream);
        }
        catch (UnauthorizedAccessException)
        {
            PrintErrorAndExit("Error: Access to \"{0}\" is denied!", inputFilePath);
        }
        catch (SystemException)
        {
            PrintErrorAndExit("Error: Can't open \"{0}\"!", inputFilePath);
        }
    }

    static void CreateOutputFile(string extension)
    {
        string filePath = Path.ChangeExtension(inputFilePath, extension);
        string newOutputFilePath = filePath;
        int fileCount = 1;

        while (File.Exists(newOutputFilePath))
        {
            string directory = Path.GetDirectoryName(filePath);
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            newOutputFilePath = string.Format("{0}{1}({2}){3}", directory, fileName, fileCount, extension);
            fileCount++;
        }

        outputFilePath = newOutputFilePath;

        try
        {
            outputFileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write);
            output = new BinaryWriter(outputFileStream);
        }
        catch (SystemException)
        {
            PrintErrorAndExit("Error: Can't create \"{0}\"!", outputFilePath);
        }
    }

    static void ReadWavHeader()
    {
        try
        {
            // Check for minimum size
            if (inputFileStream.Length < 44)
            {
                PrintErrorAndExit("Error: File \"{0}\" is too short to be a valid WAV file!", inputFilePath);
            }

            // Check for 'RIFF' header
            if (input.ReadUInt32() != 0x46464952u)
            {
                PrintErrorAndExit("Error: File \"{0}\" is not a valid WAV file!", inputFilePath);
            }

            // Check riff chunk size
            if (input.ReadUInt32() != inputFileStream.Length - 8)
            {
                PrintErrorAndExit("Error: File \"{0}\" is not a valid WAV file!", inputFilePath);
            }

            // Check for 'WAVE' format type ID
            if (input.ReadUInt32() != 0x45564157u)
            {
                PrintErrorAndExit("Error: File \"{0}\" is not a valid WAV file!", inputFilePath);
            }

            // Check for 'fmt ' subchunk
            if (input.ReadUInt32() != 0x20746D66u)
            {
                PrintErrorAndExit("Error: File \"{0}\" is not a valid WAV file!", inputFilePath);
            }

            // Check format subchunk size
            uint chunkSize = input.ReadUInt32();

            if (chunkSize != 16 && chunkSize != 18)
            {
                PrintErrorAndExit("Error: Unsupported WAV format type!");
            }

            // Check for PCM audio format
            if (input.ReadUInt16() != 1)
            {
                PrintErrorAndExit("Error: Unsupported WAV format type!");
            }

            // Check number of channels
            if (input.ReadUInt16() != 1)
            {
                PrintErrorAndExit("Error: Unsupported number of channels!");
            }

            // Check sampling rate (sample frame rate)
            if (input.ReadUInt32() != 8000)
            {
                PrintErrorAndExit("Error: Unsupported sampling rate!");
            }

            // Check byte rate 
            if (input.ReadUInt32() != 8000)
            {
                PrintErrorAndExit("Error: Unsupported byte rate!");
            }

            // Check block align (sample frame size)
            if (input.ReadUInt16() != 1)
            {
                PrintErrorAndExit("Error: Unsupported WAV format type!");
            }

            // Check bits per sample 
            if (input.ReadUInt16() != 8)
            {
                PrintErrorAndExit("Error: Unsupported bits per sample!");
            }

            // Check extension size 
            if (chunkSize == 18)
            {
                if (input.ReadUInt16() != 0)
                {
                    PrintErrorAndExit("Error: Unsupported WAV format type!");
                }
            }

            // Check for 'fact' or 'data' subchunk
            uint chunkID = input.ReadUInt32();

            // First check for 'fact' subchunk
            if (chunkID == 0x74636166u)
            {
                // Check fact subchunk size
                if (input.ReadUInt32() != 4)
                {
                    PrintErrorAndExit("Error: Unsupported WAV format type!");
                }

                // Read the number of sample frames (redundant)
                input.ReadUInt32();

                // Read 'data' subchunk
                if (input.ReadUInt32() != 0x61746164u)
                {
                    PrintErrorAndExit("Error: File \"{0}\" is not a valid WAV file!", inputFilePath);
                }
            }
            else if (chunkID != 0x61746164u)
            {
                PrintErrorAndExit("Error: File \"{0}\" is not a valid WAV file!", inputFilePath);
            }

            // Read data subchunk size
            inputDataSize = input.ReadUInt32();
        }
        catch (IOException)
        {
            PrintErrorAndExit("Error reading input file!");
        }
    }

    static void WriteWavHeader()
    {
        try
        {
            // Writing the basic WAV file header (44 bytes LE)
            output.Write(0x46464952u); // 'RIFF' chunk
            output.Write(0u);          // Riff chunk size (will be updated later)
            output.Write(0x45564157u); // 'WAVE' format type ID
            output.Write(0x20746D66u); // 'fmt ' subchunk
            output.Write(16u);         // Format subchunk size
            output.Write((ushort)1);   // PCM audio format
            output.Write((ushort)1);   // Number of channels
            output.Write(8000u);       // Sampling rate (sample frame rate)
            output.Write(8000u);       // Byte rate
            output.Write((ushort)1);   // Block align (sample frame size)
            output.Write((ushort)8);   // Bits per sample
            output.Write(0x61746164u); // 'data' subchunk
            output.Write(0u);          // Data subchunk size (will be updated later)
        }
        catch (IOException)
        {
            PrintErrorAndExit("Error writing output file!");
        }
    }

    static void ReadPlacHeader()
    {
        try
        {
            // Check for minimum size
            if (inputFileStream.Length < 16)
            {
                PrintErrorAndExit("Error: File \"{0}\" is too short to be a valid PLAC file!", inputFilePath);
            }

            // Check for 'PLAC' header
            if (input.ReadUInt32() != 0x43414C50u)
            {
                PrintErrorAndExit("Error: File \"{0}\" is not a valid PLAC file!", inputFilePath);
            }

            // Check Plac file version
            if (input.ReadUInt32() != 0)
            {
                PrintErrorAndExit("Error: File \"{0}\" is not a valid PLAC file!", inputFilePath);
            }

            // Get original data size
            outputDataSize = input.ReadUInt32();

            // Read actual data size (redundant)
            input.ReadUInt32();
        }
        catch (IOException)
        {
            PrintErrorAndExit("Error reading input file!");
        }
    }

    static void WritePlacHeader()
    {
        try
        {
            // Writing the basic PLAC file header (16 bytes LE)
            output.Write(0x43414C50u); // 'PLAC' header
            output.Write(0u);          // File version (0)
            output.Write(0u);          // Original (uncompressed) data size (will be updated later)
            output.Write(0u);          // Actual (compressed) data size (will be updated later)
        }
        catch (IOException)
        {
            PrintErrorAndExit("Error writing output file!");
        }
    }
}