﻿using System;
using System.Linq;
using System.Threading;

namespace BrainFkInterpreter
{
    class Program
    {
        public static void PrintHelp()
        {
            Console.WriteLine("BrainFkInterpreter [options] [input]");
            Console.WriteLine("Options:");
            Console.WriteLine("     -r, --realtime              Realtime input mode");
            Console.WriteLine("     -b, --binaryInput           Binary input mode");
            Console.WriteLine("     -w, --wrapBuffer            Wrap buffer instead of overflow exception");
            Console.WriteLine("     -s, --bufferSize            Sets the buffer size. Default 30000");
            Console.WriteLine("     -i, --interpret             Parse the CLI input");
            Console.WriteLine("     --raw                       process input raw without scrubbing");
            Console.WriteLine("Arguments:");
            Console.WriteLine("     input                       the *.bf file or the CLI input in interpret mode. Must be last argument");
            Environment.Exit(0);
        }

        public static void PrintError(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(msg);
            Console.ResetColor();
            Environment.Exit(1);
        }
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return;
            }
            var config = BFInterpreter.Options.Config();
            var interp = new BFInterpreter(config);
            if(CLIOptions.HandleOptions(config, args))
            {
                interp.Parse(args.Last());
            }
            else
            {
                
                string file = null;
                if (args.Last().EndsWith("bf"))
                    file = System.IO.File.ReadAllText(args.Last());
                else
                {
                    PrintError("Error: Only *.bf files can be read by this interpreter");
                    return;
                }
                CLIOptions.HandleFileOptions(config, file.Replace("\n", " ").Replace("\t", " ").Replace("\r", ""));
                interp.Parse(file);
            }
            
        }
    }

    class CLIOptions
    {
        public static bool HandleFileOptions(BFInterpreter.Options opts, string file)
        {
            if (file.StartsWith("[options:"))
                HandleOptions(opts, file.Substring(1, file.IndexOf("]")).Split(" ", StringSplitOptions.RemoveEmptyEntries));
            return false;
        }
        public static bool HandleOptions(BFInterpreter.Options opts, string[] args)
        {
            
            bool interpret = false;
            foreach (var arg in args)
            {
                if (arg.StartsWith("-"))
                {
                    interpret = SetOption(opts, arg.Replace("--", "").Replace("-", "")) ?? interpret;
                }
            }
            return interpret;
        }

        private static bool? SetOption(BFInterpreter.Options opts, string arg)
        {
            var split = arg.Split("=");
            switch (split[0])
            {
                case "r":
                case "realtime":
                    opts.ToggleRealTimeMode();
                    break;
                case "b":
                case "binaryInput":
                    opts.ToggleBinaryInput();
                    break;
                case "w":
                case "wrapBuffer":
                    opts.ToggleWrapBuffer();
                    break;
                case "s":
                case "bufferSize":
                    opts.SetBufferSize(int.Parse(split[1]));
                    break;
                case "raw":
                    opts.ToggleOptimizeInput();
                    break;
                case "i":
                case "interpret":
                    return true;
                case "h":
                case "help":
                    Program.PrintHelp();
                    break;
            }

            return null;
        }
    }

    public class BFInterpreter
    {
        public class Options
        {
            public int BufferSize { get; protected set; }
            public bool WrapBuffer { get; protected set; }
            public bool BinaryInput { get; protected set; }
            public bool OptimizeInput { get; protected set; }
            public bool RealtimeMode { get; protected set; }

            private Options()
            {
                this.BufferSize = 30000;
                this.BinaryInput = false;
                this.WrapBuffer = false;
                this.OptimizeInput = true;
                this.RealtimeMode = false;
            }
            public Options ToggleWrapBuffer()
            {
                this.WrapBuffer = !this.WrapBuffer;
                return this;
            }
            public Options ToggleBinaryInput()
            {
                this.BinaryInput = !this.BinaryInput;
                return this;
            }
            public Options ToggleOptimizeInput()
            {
                this.OptimizeInput = !this.OptimizeInput;
                return this;
            }
            public Options ToggleRealTimeMode()
            {
                this.RealtimeMode = !this.RealtimeMode;
                return this;
            }
            public Options SetBufferSize(int size)
            {
                this.BufferSize = size;
                return this;
            }
            public static Options Config()
            {
                return new Options();
            }
        }
        public const char PtrLeft = '<';
        public const char PtrRight = '>';
        public const char StdIn = ',';
        public const char StdOut = '.';
        public const char BgnLoop = '[';
        public const char EndLoop = ']';
        public const char Increment = '+';
        public const char Decrement = '-';
        private byte[] _buffer;
        private int _ptr;

        public Options InterpreterOptions { get; }

        public BFInterpreter(Options options = null)
        {
            this.InterpreterOptions = options ?? Options.Config().SetBufferSize(30000);
            _buffer = new byte[this.InterpreterOptions.BufferSize];
            _ptr = 0;
        }

        public void Parse(string input)
        {
            var tokens = input.ToCharArray();
            if (this.InterpreterOptions.OptimizeInput)
            {
                tokens = tokens.Where(x => new[] { PtrLeft, PtrRight, StdIn, StdOut, BgnLoop, EndLoop, Increment, Decrement }.Contains(x)).ToArray();
            }
            var i = 0;
            while (i < tokens.Length)
            {
                switch (tokens[i])
                {
                    case PtrLeft:
                        if (_ptr != 0)
                            _ptr--;
                        else if (InterpreterOptions.WrapBuffer)
                            _ptr = InterpreterOptions.BufferSize - 1;
                        else
                            throw new IndexOutOfRangeException("Index fell below the minimum (0)");
                        break;
                    case PtrRight:
                        if (_ptr < this.InterpreterOptions.BufferSize - 1)
                            _ptr++;
                        else if (InterpreterOptions.WrapBuffer)
                            _ptr = 0;
                        else
                            throw new IndexOutOfRangeException($"Index is above the buffer size ({this.InterpreterOptions.BufferSize})");
                        break;
                    case StdIn:
                        _buffer[_ptr] = this.InterpreterOptions.RealtimeMode ? (byte)CaptureRealTime() : this.InterpreterOptions.BinaryInput ? Convert.ToByte(Console.ReadLine(), 16) : (byte)Console.ReadLine()[0];
                        break;
                    case StdOut:
                        Console.Write((char)_buffer[_ptr]);
                        break;
                    case Increment:
                        _buffer[_ptr]++;
                        break;
                    case Decrement:
                        _buffer[_ptr]--;
                        break;
                    case BgnLoop:
                        if (this._buffer[_ptr] == 0)
                        {
                            i = FindClosedBracket(tokens, i);
                        }
                        break;
                    case EndLoop:
                        if (this._buffer[_ptr] != 0)
                        {
                            i = FindOpenBracket(tokens, i);
                        }
                        break;
                }
                i++;
            }
            ClearBuffer();
        }

        private void ClearBuffer()
        {
            Array.Clear(this._buffer, 0, this.InterpreterOptions.BufferSize);
            this._ptr = 0;
        }

        private int FindOpenBracket(char[] tokens, int closeIndex)
        {
            int openIndex = closeIndex;
            int counter = 1;
            while (counter > 0)
            {
                char c = tokens[--openIndex];
                if (c == BgnLoop)
                    counter--;
                else if (c == EndLoop)
                    counter++;
            }
            return openIndex;
        }

        private int FindClosedBracket(char[] tokens, int openIndex)
        {
            int closeIndex = openIndex;
            int counter = 1;
            while (counter > 0)
            {
                char c = tokens[++closeIndex];
                if (c == EndLoop)
                    counter--;
                else if (c == BgnLoop)
                    counter++;
            }
            return closeIndex;
        }

        private char CaptureRealTime()
        {
            var cki = new ConsoleKeyInfo();
            while (Console.KeyAvailable == false)
                Thread.Sleep(250); // Loop until input is entered.
            cki = Console.ReadKey(true);
            return cki.KeyChar;
        }
    }

}
