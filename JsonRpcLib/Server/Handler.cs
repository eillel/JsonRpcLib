﻿using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace JsonRpcLib.Server
{
    public partial class JsonRpcServer
    {
        private class HandlerInfo
        {
            public object Object { get; internal set; }
            public MethodInfo Method { get; internal set; }
            public Delegate Call { get; internal set; }
        }

        readonly ConcurrentDictionary<string, HandlerInfo> _handlers = new ConcurrentDictionary<string, HandlerInfo>();

        public void RegisterHandlers(object handler, string prefix = "")
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (prefix.Any(c => char.IsWhiteSpace(c)))
                throw new ArgumentException("Prefix string can not contain any whitespace");

            foreach (var m in handler.GetType().GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public))
            {
                var name = prefix + m.Name;
                if (_handlers.TryGetValue(name, out var existing))
                {
                    throw new JsonRpcException($"The method '{name}' is already handled by the class {existing.Object.GetType().Name}");
                }

                var info = new HandlerInfo {
                    Object = handler,
                    Method = m,
                    Call = Reflection.CreateMethod(handler, m)
                };
                _handlers.TryAdd(name, info);
                Debug.WriteLine($"Added handler for '{name}' as {handler.GetType().Name}.{m.Name}");
            }
        }

        public void RegisterHandlers(Type type, string prefix = "")
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (prefix.Any(c => char.IsWhiteSpace(c)))
                throw new ArgumentException("Prefix string can not contain any whitespace");

            foreach (var m in type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Public))
            {
                var name = prefix + m.Name;
                if (_handlers.TryGetValue(name, out var existing))
                {
                    throw new JsonRpcException($"The method '{name}' is already handled by the class {existing.Object.GetType().Name}");
                }

                var info = new HandlerInfo {
                    Method = m,
                    Call = Reflection.CreateMethod(m)
                };
                _handlers.TryAdd(name, info);
                Debug.WriteLine($"Added handler for '{name}' as static {type.Name}.{m.Name}");
            }
        }

        internal void ExecuteHandler(ClientConnection client, int id, string method, object[] args)
        {
            if (_handlers.TryGetValue(method, out var info))
            {
                try
                {
                    bool hasOptionalParameters = false;
                    if (args != null)
                    {
                        FixupArgs(info.Method, ref args, out hasOptionalParameters);
                    }

                    object result = null;
                    if (hasOptionalParameters)
                    {
                        // Use reflection invoke instead of delegate because we have optional parameters
                        if (info.Object != null)
                        {
                            // Instance function
                            result = info.Method.Invoke(info.Object,
                                BindingFlags.OptionalParamBinding | BindingFlags.InvokeMethod | BindingFlags.CreateInstance, null, args, null);
                        }
                        else
                        {
                            // Static function
                            result = info.Method.Invoke(null, BindingFlags.OptionalParamBinding | BindingFlags.InvokeMethod, null, args, null);
                        }
                    }
                    else
                    {
                        result = info.Call.DynamicInvoke(args);
                    }

                    if (id == -1)
                        return;     // Was a notify, so don't reply

                    if (info.Method.ReturnParameter.ParameterType != typeof(void))
                    {
                        var response = new Response<object>() {
                            Id = id,
                            Result = result
                        };
                        var json = Serializer.Serialize(response);
                        client.Write(json);
                    }
                    else
                    {
                        var response = new Response() {
                            Id = id
                        };
                        var json = Serializer.Serialize(response);
                        client.Write(json);
                    }
                }
                catch (Exception ex)
                {
                    var response = new Response() {
                        Id = id,
                        Error = new Error() { Code = -1, Message = $"Handler '{method}' threw an exception: {ex.Message}" }
                    };
                    var json = Serializer.Serialize(response);
                    client.Write(json);
                }
            }
            else
            {
                //
                // Unknown method
                //
                var response = new Response() {
                    Id = id,
                    Error = new Error() { Code = -32601, Message = $"Unknown method '{method}'" }
                };
                var json = Serializer.Serialize(response);
                client.Write(json);
            }
        }

        private void FixupArgs(MethodInfo method, ref object[] args, out bool notAllArgsAreThere)
        {
            notAllArgsAreThere = false;
            var p = method.GetParameters();
            int neededArgs = p.Count(x => !x.HasDefaultValue);
            if (neededArgs > args.Length)
                throw new JsonRpcException($"Argument count mismatch (Expected at least {neededArgs}, but got only {args.Length}");

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == null)
                    continue;
                var at = args[i].GetType();
                if (at == p[i].ParameterType)
                    continue;
                if (at.IsPrimitive)
                    args[i] = Convert.ChangeType(args[i], p[i].ParameterType);
                else if (at == typeof(string))
                {
                    if (p[i].ParameterType == typeof(TimeSpan))
                        args[i] = TimeSpan.Parse((string)args[i]);
                }
                else if (at == typeof(JArray))
                {
                    var a = args[i] as JArray;
                    args[i] = a.ToObject(p[i].ParameterType);
                }
            }

            if (args.Length < p.Length)
            {
                args = args.Concat(Enumerable.Repeat(Type.Missing, p.Length - args.Length)).ToArray();
                notAllArgsAreThere = true;
            }
        }
    }
}
