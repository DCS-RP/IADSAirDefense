﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using RurouniJones.Dcs.Grpc.V0.Controller;
using RurouniJones.Dcs.Grpc.V0.Mission;
using RurouniJones.Dcs.Grpc.V0.Unit;
using RurouniJones.SimpleAirDefense.Grpc.Cache;
using RurouniJones.SimpleAirDefense.Shared.Interfaces;
using RurouniJones.SimpleAirDefense.Shared.Models;
using SimpleAirDefense.Encyclopedia;

namespace RurouniJones.SimpleAirDefense.Grpc
{
    public class RpcClient : IRpcClient
    {
        public ConcurrentQueue<Unit> UpdateQueue { get; set; }

        public string HostName { get; set; }
        public int Port { get; set; }

        private readonly ILogger<RpcClient> _logger;
        private readonly UnitDescriptorCache _descriptorCache;

        public RpcClient(ILogger<RpcClient> logger, UnitDescriptorCache descriptorCache)
        {
            _logger = logger;
            _descriptorCache = descriptorCache;
        }

        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var channel = GrpcChannel.ForAddress($"http://{HostName}:{Port}");
            var client = new MissionService.MissionServiceClient(channel);
            try
            {
                var units = client.StreamUnits(new StreamUnitsRequest
                {
                    PollRate = 1,
                    MaxBackoff = 30
                }, null, null, stoppingToken);
                await foreach (var update in units.ResponseStream.ReadAllAsync(stoppingToken))
                {
                    switch (update.UpdateCase)
                    {
                        case StreamUnitsResponse.UpdateOneofCase.None:
                            //No-op
                            break;
                        case StreamUnitsResponse.UpdateOneofCase.Unit:
                            var sourceUnit = update.Unit;
                            UpdateQueue.Enqueue(new Unit(this)
                            {
                                Coalition = (int)sourceUnit.Coalition,
                                Id = sourceUnit.Id,
                                Name = sourceUnit.Name,
                                Position = new Position(sourceUnit.Position.Lat, sourceUnit.Position.Lon),
                                Altitude = sourceUnit.Position.Alt,
                                Callsign = sourceUnit.Callsign,
                                Type = sourceUnit.Type,
                                Player = sourceUnit.PlayerName,
                                GroupName = sourceUnit.Group.Name,
                                Speed = sourceUnit.Velocity.Speed,
                                Heading = sourceUnit.Velocity.Heading,
                                Symbology = new MilStd2525d((int) sourceUnit.Coalition, Repository.GetUnitEntryByDcsCode(sourceUnit.Type)?.MilStd2525D)
                            });
                            _logger.LogTrace("Enqueue unit update {unit}", sourceUnit);
                            break;
                        case StreamUnitsResponse.UpdateOneofCase.Gone:
                            var deletedUnit = update.Gone;
                            UpdateQueue.Enqueue(new Unit(null)
                            {
                                Id = deletedUnit.Id,
                                Name = deletedUnit.Name,
                                Deleted = true
                            });
                            _logger.LogTrace("Enqueue unit deletion {unit}", deletedUnit);
                            break;
                        default:
                            _logger.LogWarning("Unexpected UnitUpdate case of {case}", update.UpdateCase);
                            break;
                    }
                }
            }
            catch (RpcException ex)
            {
                if (ex.Status.StatusCode == StatusCode.Cancelled)
                {
                    _logger.LogInformation("Shutting down gRPC connection due to {reason}", ex.Status.Detail);
                }
                else
                {
                    _logger.LogWarning(ex, "gRPC Exception");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "gRPC Exception");
            }
        }

        public async Task<UnitDescriptor> GetUnitDescriptorAsync(string name, string type)
        {
            _logger.LogTrace("{name} ({type}) Retrieving Descriptor", name, type);

            var cachedDescriptor = _descriptorCache.GetDescriptor(type);
            if (cachedDescriptor != null)
            {
                _logger.LogTrace("{name} ({type}) Descriptor Cache hit", name, type);
                return cachedDescriptor;
            }

            _logger.LogTrace("{name} ({type}) Descriptor Cache miss", name, type);

            using var channel = GrpcChannel.ForAddress($"http://{HostName}:{Port}");
            var client = new UnitService.UnitServiceClient(channel);

            try
            {
                var descriptor = await client.GetDescriptorAsync(new GetDescriptorRequest()
                {
                    Name = name
                }).ResponseAsync;

                var unitDescriptor = new UnitDescriptor
                {
                    Attributes = descriptor.Attributes.ToList()
                };

                await _descriptorCache.AddDescriptorToCacheAsync(type, unitDescriptor);
                return unitDescriptor;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "gRPC Exception");
                return null;
            }
        }

        public async Task SetAlarmStateAsync(string unitName, string groupName, int alarmState)
        {
            using var channel = GrpcChannel.ForAddress($"http://{HostName}:{Port}");
            var client = new ControllerService.ControllerServiceClient(channel);
            // TODO REMOVE BEFORE PR
            // TODO REMOVE BEFORE PR
            // TODO REMOVE BEFORE PR
            // TODO REMOVE BEFORE PR
            // _logger.LogWarning(string.Format("SetAlarmStateAsync unitName {0} groupName {1} alarmState {2}", unitName, groupName, alarmState));    
            
            try
            {
                var request = new SetAlarmStateRequest
                {
                    AlarmState = (SetAlarmStateRequest.Types.AlarmState)alarmState
                };
                if (unitName != null)
                {
                    request.UnitName = unitName;
                }
                else
                {
                    request.GroupName = groupName;
                }

                await client.SetAlarmStateAsync(request).ResponseAsync;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "gRPC Exception");
            }
        }
    }
}
