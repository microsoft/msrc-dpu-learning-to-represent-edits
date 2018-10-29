using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocoptNet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DumpCommitData
{
    class Program
    {
        private const string usage = @"Idiomatic Change Mining.

    Usage:
      DumpCommitData.exe get_python_input <input_file> <output_file> <grammar_file>

    Options:
      -h --help     Show this screen.

    ";

        private static void Main(string[] args)
        {
            var arguments = new Docopt().Apply(usage, args, exit: true);
            
            Console.WriteLine("Input arguments:");
            foreach (var argument in arguments)
            {
                Console.WriteLine("{0} = {1}", argument.Key, argument.Value);
            }

            if (arguments["get_python_input"].IsTrue)
            {
                Pipeline.DumpRevisionDataForNeuralTraining(arguments["<input_file>"].ToString(),
                    arguments["<output_file>"].ToString(),
                    arguments["<grammar_file>"].ToString());
            }
        }
    }
}
