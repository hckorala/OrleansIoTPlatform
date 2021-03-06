﻿using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Runtime;
using OrleansIoTPlatform.GrainInterfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

using SwinIotFramework;

namespace Client
{
    public class Program
    {
        const int initializeAttemptsBeforeFailing = 5;
        private static int attempt = 0;

        static int Main(string[] args)
        {
            return RunMainAsync().Result;
        }

        private static async Task<int> RunMainAsync()
        {
            try
            {
                // Purpose of using using is to dispose the object as soon as it goes out of scope 
                using (var client = await StartClientWithRetries())
                {
                    await DoClientWork(client);
                    Console.ReadKey();
                }

                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.ReadKey();
                return 1;
            }
        }

        private static async Task<IClusterClient> StartClientWithRetries()
        {
            attempt = 0;
            IClusterClient client;
            client = new ClientBuilder()
                    .UseLocalhostClustering()
                    .Configure<ClusterOptions>(options =>
                    {
                        options.ClusterId = "dev";
                        options.ServiceId = "OrleansIoTApp";
                    })
                    .ConfigureLogging(logging => logging.AddConsole())
                    .Build();

            await client.Connect(RetryFilter);
            Console.WriteLine("Client successfully connect to silo host");
            return client;
        }

        private static async Task<bool> RetryFilter(Exception exception)
        {
            if (exception.GetType() != typeof(SiloUnavailableException))
            {
                Console.WriteLine($"Cluster client failed to connect to cluster with unexpected error.  Exception: {exception}");
                return false;
            }
            attempt++;
            Console.WriteLine($"Cluster client attempt {attempt} of {initializeAttemptsBeforeFailing} failed to connect to cluster.  Exception: {exception}");
            if (attempt > initializeAttemptsBeforeFailing)
            {
                return false;
            }
            await Task.Delay(TimeSpan.FromSeconds(4));
            return true;
        }

        private static async Task DoClientWork(IClusterClient client)
        {
            var testPlacementHolder = client.GetGrain<IPlacementHolder>(Guid.NewGuid());
            SiloAddress test = await testPlacementHolder.GetSiloAddress(0);

            Console.WriteLine("Silo Address : ", test);

            var reducerActor = client.GetGrain<IReducer>(0);
            List<Task> forks = new List<Task>();
            List<string> myData = new List<string>();
            var fileName = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), @"Resources\AccTestData.csv");

            using (StreamReader fileContent = new StreamReader(fileName))
            {
                string headerLine = fileContent.ReadLine();
                string line;
                while ((line = fileContent.ReadLine()) != null)
                {
                    myData.Add(line);
                }
            }

            Console.WriteLine("Input Data Line by Line\n");
            Console.WriteLine(myData.ToString());
            foreach (var t in myData.ToArray())
            {
                Console.WriteLine("\n{0}\n", t);
            }

            foreach (var w in myData.ToArray())
            {
                Task subProcess = ProcessSubResultAsync(client, reducerActor, w);
                forks.Add(subProcess);
            }

            await Task.WhenAll(forks);
            var results = await reducerActor.GetResults();
            Console.WriteLine("Reducer output : Aggregated Values\n");
            Console.WriteLine("\n{0}\n", results.ToString());
        }

        private static async Task ProcessSubResultAsync( IClusterClient client, IReducer proxy, string partialData)
        {
            var mapper = client.GetGrain<IMapper>(Guid.NewGuid());
            AccelerationDataPerSec subResult = await mapper.MapAccelerationData(partialData);
            Console.WriteLine("\n\n{0}\n\n", subResult);
            await proxy.ReduceAccelerationData(subResult);
        }
    }
}
