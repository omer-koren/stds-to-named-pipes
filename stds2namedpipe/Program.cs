using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace stds2namedpipe
{
    class Program
    {
        private static CommandOption _stdoutOption;
        private static CommandOption _stdinOption;
        private static ManualResetEventSlim _done;
        private static CommandOption _stderrOption;
        private static CommandOption _logsOption;
        private static TextWriter _logger;

        static void Main(string[] args)
        {

            var app = new CommandLineApplication(true)
            {
                Description = "Tool to redirects its stdout,stdin,stderr to named pipes",
                Name = "stds2namedpipe",
            };

            app.HelpOption("--help");

            var assemblyName = Assembly.GetEntryAssembly().GetName();
            app.VersionOption("--version", assemblyName.Name + " " + assemblyName.Version.ToString());

            _stdoutOption = app.Option("--out|-o", "Stdout pipe name", CommandOptionType.SingleValue);
            _stdinOption = app.Option("--in|-i", "Stdin pipe name", CommandOptionType.SingleValue);
            _stderrOption = app.Option("--err|-e", "Stderr pipe name", CommandOptionType.SingleValue);
            _logsOption = app.Option("--logs|-l", "Log file", CommandOptionType.SingleValue);

             var _cts = new CancellationTokenSource();
            _done = new ManualResetEventSlim(false);

            
            if (_logsOption.HasValue())
                _logger = File.CreateText(_logsOption.Value());
            else
                _logger = new StreamWriter(Stream.Null);

            Console.CancelKeyPress += (sender, eventArgs) =>
            {

                if (!_cts.IsCancellationRequested)
                {
                    _cts.Cancel();
                }

                _done.Wait(2000);
                eventArgs.Cancel = true;
            };

            app.OnExecute(async ()=> await RunAsync(_cts.Token));

            using (_logger)
            {
                app.Execute(args);
            }
           
        }

        private static  async Task CreatePipeServer(string name, PipeDirection direction, Func<Stream> source, CancellationToken cancellation)
        {
            
            Stream stream = null;

            while (!cancellation.IsCancellationRequested)
            {
                var server = new NamedPipeServerStream(name, direction, 1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);

                try
                {
                    _logger.WriteLine($"[{name}] Waiting for connection...");

                    await server.WaitForConnectionAsync(cancellation);

                    _logger.WriteLine($"[{name}] Connection accpeted");

                    stream = source();

                    _logger.WriteLine($"[{name}] Redirect stream...");

                    switch (direction)
                    {
                        case PipeDirection.In:
                            await server.CopyToAsync(stream, 4096, cancellation);
                            break;
                        case PipeDirection.Out:
                            await stream.CopyToAsync(server, 4096, cancellation);
                            break;
                        default:
                            throw new NotSupportedException("Unsupported pipe direction server");
                    }   
                }
                catch (NotSupportedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.WriteLine($"[{name}] Failed to redirect streams: {ex.ToString()}");
                }
                finally
                {
                    server?.Dispose();
                }
            }

            _logger.WriteLine($"[{name}] Finished to redirect");

        }

        private async static Task<int> RunAsync(CancellationToken cancellation)
        {
            var tasks = new List<Task>();


            
            if (_stdoutOption.HasValue())
            {
                tasks.Add(Task.Run(async () => await CreatePipeServer(_stdoutOption.Value(),
                                                                     PipeDirection.In,
                                                                     () => Console.OpenStandardOutput(),
                                                                     cancellation),
                                                                            cancellation));
            }


            if (_stdinOption.HasValue())
            {
                tasks.Add(Task.Run(async () => await CreatePipeServer(_stdinOption.Value(),
                                                                  PipeDirection.Out,
                                                                  () => Console.OpenStandardInput(),
                                                                  cancellation),
                                                                         cancellation));
            }

            if (_stderrOption.HasValue())
            {
                tasks.Add(Task.Run(async () => await CreatePipeServer(_stderrOption.Value(),
                                                                  PipeDirection.In,
                                                                  () => Console.OpenStandardError(),
                                                                  cancellation),
                                                                         cancellation));
            }

            if (tasks.Count <= 0)
            {
                Console.WriteLine("No stds to redirect");
                return 1;
            }

            await Task.WhenAll(tasks);

            _done.Set();

            return 0;
        }
    }

    
}
