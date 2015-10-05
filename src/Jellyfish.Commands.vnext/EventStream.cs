﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using System.Collections.Concurrent;
using Jellyfish.Configuration;
using System.Threading.Tasks;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;
using Jellyfish.Commands;

namespace Microsoft.Framework.DependencyInjection
{
    public static class JellyfishExtensions 
    {    
        public static IApplicationBuilder UseJellyfish(this IApplicationBuilder builder)
        {
            builder.UseMiddleware<Jellyfish.Commands.EventStreamHandler>();
            return builder;
        }

        public static void AddJellyfish(this IServiceCollection services)
        {
            services.AddScoped<IJellyfishContext, JellyfishContext>();
        }
    }
}

namespace Jellyfish.Commands
{
    public class EventStreamHandler
    {
        private int _nbConnections;
        private IDynamicProperty<int> maxConcurrentConnection = DynamicProperties.Instance.CreateOrUpdateProperty("jellyfish.stream.maxConcurrentConnections", 5);
        private RequestDelegate _next;
         
         
        public EventStreamHandler(RequestDelegate next) {
            _next = next;
        } 
     
        public async Task Invoke(HttpContext context) {
            var url = context.Request.Path.Value ?? string.Empty;
            if (!url.StartsWith("/jellyfish.stream", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }
          
            var nb = Interlocked.Increment(ref _nbConnections);
            CancellationTokenSource token = null;
            try 
            {
                if( nb > maxConcurrentConnection.Value )
                {
                    context.Response.StatusCode = 503;
                    context.Response.ContentType = "text/plain";
                    var buffer = Encoding.ASCII.GetBytes("Max concurrent connections reached.");
                    context.Response.Body.Write(buffer, 0, buffer.Length);
                    return;
                }
                
                int delay = 1000;
                var pos = url.IndexOf("?delay=");
                if( pos > 0) {
                    try {
                        delay = Int32.Parse(url.Substring(pos+7));
                    }
                    catch {}
                }
                
                token = new CancellationTokenSource();
                var poller = new MetricsPoller(delay, token.Token);
                poller.Start();

                context.Response.ContentType = "text/event-stream";
                context.Response.Headers.Add("Connection", new string[] { "keep-alive" });
                context.Response.Headers.Add("Cache-Control", new string[] { "no-cache, no-store, max-age=0, must-revalidate" });
                context.Response.Headers.Add("Pragma", new string[] { "no-cache" });
                context.Response.StatusCode = 200;
                await context.Response.Body().FlushAsync();
                
                var ping = Encoding.UTF8.GetBytes("ping: \n");
                while (!context.RequestAborted.IsCancellationRequested)
                {
                    var events = poller.GetJsonMetrics();

                    if (events.Count() == 0)
                    {
                        context.Response.Body.Write(ping, 0, ping.Length);
                    }
                    else
                    {
                        foreach (var json in events)
                        {
                            var bytes = Encoding.UTF8.GetBytes(String.Format("data: {0}\n", json));
                            context.Response.Body.Write(bytes, 0, bytes.Length);
                        }
                    }
                  
                    await context.Response.Body.FlushAsync(token.Token);

                    await Task.Delay(delay, token.Token);
                }
            }
            catch { }           
            finally
            {
                if (token != null && !token.IsCancellationRequested)
                    token.Cancel();
                Interlocked.Decrement(ref _nbConnections);
            }
        }         
    }

    class MetricsPoller
    {
        private int delay;
        private CancellationToken token;
        private ConcurrentQueue<string> events = new ConcurrentQueue<string>();

        public MetricsPoller(int delay, CancellationToken token)
        {
            this.delay = delay;
            this.token = token;
        }

        public void Start()
        {
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    foreach (var entry in Jellyfish.Commands.Metrics.CommandMetricsFactory.GetInstances())
                    {
                        try
                        {
                            var publisher = new Jellyfish.Commands.Metrics.Publishers.JsonMetricsPublisherCommand(entry);
                            publisher.Run(UpdateEvent);
                        }
                        catch { }
                    }

                    await Task.Delay(delay);
                }
            });
        }

        private void UpdateEvent(string evt)
        {
            events.Enqueue(evt);
        }

        internal IEnumerable<string> GetJsonMetrics()
        {
            var tmp = Interlocked.Exchange(ref events, new ConcurrentQueue<string>());
            return tmp.ToList();
        }
    }
}