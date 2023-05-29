using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geo.Geodesy;
using Microsoft.Extensions.Logging;
using RurouniJones.SimpleAirDefense.Shared.Interfaces;
using RurouniJones.SimpleAirDefense.Shared.Models;

namespace RurouniJones.SimpleAirDefense.Core
{
    public class Defender
    {
        // These values are taken from Wheelyjoes IADS script.
        // https://github.com/wheelyjoe/DCS-Scripts/blob/master/IADS.lua
        private readonly Dictionary<string, int> _samRanges = new()
        {
            { "Kub 1S91 str", 52000 },
            { "S-300PS 40B6M tr", 100000 },
            { "Osa 9A33 ln", 25000 },
            { "snr s-125 tr", 60000 },
            { "SNR_75V", 65000 },
            { "Dog Ear radar", 26000 },
            { "SA-11 Buk LN 9A310M1", 43000 },
            { "Hawk tr", 60000 },
            { "Tor 9A331", 50000 },
            { "rapier_fsa_blindfire_radar", 6000 },
            { "Patriot str", 100000 },
            { "Roland ADS", 10000 },
            { "HQ-7_STR_SP", 12500 },
            { "ZSU-23-4 Shilka", 1000 },
            { "2S6 Tunguska", 50000},
            { "RPC_5N62V", 75000}
        };

        private enum AlarmState
        {
            Auto = 1,
            Green = 2,
            Red = 3
        }

        /*
         * Configuration for the GameServer including DB and RPC information
         */
        public GameServer GameServer { get; set; }

        public IADSConfig IadsConfig { get; set; }
        
        /*
         * The RPC client that connects to the server and receives the unit updates
         * to put into the update queue
         */
        private readonly IRpcClient _rpcClient;

        private readonly ILogger<Defender> _logger;

        private readonly Dictionary<uint, Unit> _units = new();

        public Defender(ILogger<Defender> logger, IRpcClient rpcClient)
        {
            _logger = logger;
            _rpcClient = rpcClient;
        }

        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _rpcClient.HostName = GameServer.Rpc.Host;
            _rpcClient.Port = GameServer.Rpc.Port;

            // var _iadsconfig_name                = IadsConfig.Name;
            // var _iadsconfig_IADSEnable          = IadsConfig.IADSEnable;
            // var _iadsconfig_IADSEWRARMDetection = IadsConfig.IADSEWRARMDetection;
            // For now we will hardcode these vars until we can figure out how to get them into the config file
            var _iadsconfig_name                = "IADSCONFIG-1";
            var _iadsconfig_IADSEnable          = true; // If true IADS script is active
            var _iadsconfig_IADSEWRARMDetection = true; //1 = EWR detection of ARMs on, 0 = EWR detection of ARMs off
            
            
            // TODO remove before PR
            // TODO remove before PR
            // TODO remove before PR
            // TODO remove before PR
            // See if we can pull the iadsconfig data
            _logger.LogDebug("IadsConfig Name {_iadsconfig_name} IADSEnable {_iadsconfig_IADSEnable} IADSEWRARMDetection {_iadsconfig_IADSEWRARMDetection}",_iadsconfig_name,_iadsconfig_IADSEnable, _iadsconfig_IADSEWRARMDetection );
            
            _logger.LogInformation("[{server}] Defender Processing starting", GameServer.ShortName);
            while (!stoppingToken.IsCancellationRequested)
            {
                var defenderTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var defenderToken = defenderTokenSource.Token;

                /*
                 * A queue containing all the unit updates to be processed. We populate
                 * this queue in a separate thread to make sure that slowdowns in unit
                 * processing do not impact the rate at which we can receive unit updates
                 *
                 * We clear the queue each time we connect
                 */
                var queue = new ConcurrentQueue<Unit>();
                _rpcClient.UpdateQueue = queue;

                var tasks = new[]
                {
                    _rpcClient.ExecuteAsync(defenderToken), // Get the events and put them into the queue
                    ProcessQueue(queue, defenderToken), // Process the queue events into the units dictionary
                    MonitorAirspace(defenderToken) // Main processing
                };
                await Task.WhenAny(tasks); // If one task finishes (usually when the RPC client gets
                                           // disconnected on mission restart
                _logger.LogInformation("[{server}] Defender Processing stopping", GameServer.ShortName);
                defenderTokenSource.Cancel(); // Then cancel all of the other tasks
                // Then we wait for all of them to finish before starting the loop again.
                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (Exception)
                {
                    // No-op. Exceptions have already been logged in the task
                }

                _logger.LogInformation("[{server}] Defender Processing stopped", GameServer.ShortName);

                // Wait before trying again unless the entire service is shutting down.
                await Task.Delay((int)TimeSpan.FromSeconds(10).TotalMilliseconds, stoppingToken);
                _logger.LogInformation("[{server}] Defender Processing restarting", GameServer.ShortName);
            }
        }

        private async Task ProcessQueue(ConcurrentQueue<Unit> queue, CancellationToken scribeToken)
        {

            while (!scribeToken.IsCancellationRequested)
            {
                queue.TryDequeue(out var unit);
                if (unit == null)
                {
                    await Task.Delay(5, scribeToken);
                    continue;
                }

                if (unit.Deleted)
                {
                    _units.Remove(unit.Id, out _);
                }
                else
                {
                    _units[unit.Id] = unit;
                }
            }
        }

        private async Task MonitorAirspace(CancellationToken defenderToken)
        {
            _logger.LogDebug("[{server}] Airspace monitoring started", GameServer.ShortName);
            while (!defenderToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug("[{server}] Entering Monitoring Loop", GameServer.ShortName);

                    // Skip if there are no units
                    if (_units.Values.Count == 0)
                    {
                        _logger.LogInformation("[{server}] No Units found. Skipping", GameServer.ShortName);
                        await Task.Delay(5000, defenderToken);
                        continue;
                    }
                    else {
                        //TODO Remove before pr
                        //TODO Remove before pr
                        //TODO Remove before pr
                        //TODO Remove before pr
                        // This will loop through every sam on redfor
                            // then we need to loop through the units they detect
                        foreach (var allunits in _units.Values) {
                            
                            //Debugging all the values we have in attributes
                            // if (allunits.Name != null && allunits.Type != null && allunits.Attributes != null) {
                            //     _logger.LogInformation(
                            //         "**********************Unit: {unitname} | {type}\nAttributes is {attributes}\n\n",
                            //         allunits.Name, allunits.Type, allunits.Attributes);
                            // }
                            

                            // If a SAM or EWR look at what we see!
                            if (allunits.Attributes.Contains("SAM TR") || allunits.Attributes.Contains("EWR")) {
                                _logger.LogInformation(string.Format("Unit Name: {0} " +
                                                                     "Coalition {1} Type {2}"
                                    , allunits.Name, allunits.Coalition, allunits.Type));

                                // var _detections = allunits.
                                _logger.LogInformation("all units has {unit} units", allunits.DetectedTargetsResponse.Contacts.Count);
                                
                                //loop over detections
                                foreach (var detections in allunits.DetectedTargetsResponse.Contacts) {
                                    var _unit   = detections.Unit;
                                    var _weapon = detections.Weapon;
                                    if (_unit != null) {
                                        _logger.LogInformation("Unit: {unit}", _unit);
                                    }

                                    if (_weapon != null) {
                                        _logger.LogInformation("Unit {unit} Weapon is not Null! {weapon}", _unit, _weapon);
                                    }
                                }
                            }
                        }
                    }

                    var alarmStates = new Dictionary<string, int>();
                    // Check to see if there are any active EWRs
                    var ewrsPresent = _units.Values.ToList().Any(u => u.Attributes.Contains("EWR"));

                    // If EWR are NOT present
                    if (!ewrsPresent)
                    {
                        _logger.LogInformation("[{server}] No EWRs found. Turning on all SAM sites", GameServer.ShortName);
                        // Build a list of all SamSites
                        foreach (var samSite in _units.Values.Where(u => u.Attributes.Contains("SAM TR")))
                        {
                            _logger.LogDebug("[{server}] {unitName} ({unitType}), {groupName}: Turning on radar",
                                GameServer.ShortName, samSite.Name, samSite.Type, samSite.GroupName);
                            samSite.AlarmState = (int) AlarmState.Red;
                        }
                    }
                    // If EWR are present
                    else
                    {
                        _logger.LogDebug("[{server}] EWR sites found", GameServer.ShortName);
                        // Build a list of all SamSites
                        foreach (var samSite in _units.Values.Where(u => u.Attributes.Contains("SAM TR")))
                        {
                            _logger.LogDebug("[{server}] {unitName} ({unitType}), {groupName}: Checking if targets in activation range",
                                GameServer.ShortName, samSite.Name, samSite.Type, samSite.GroupName);

                            var samSitePosition =
                                new Geo.Coordinate(samSite.Position.Latitude, samSite.Position.Longitude);
                            var targetsInRange = _units.Values.Count(u =>
                            {
                                var unitPosition = new Geo.Coordinate(u.Position.Latitude, u.Position.Longitude);
                                return u.Coalition != samSite.Coalition
                                       && u.Symbology.SymbolSet == MilStd2525d.Enums.SymbolSet.Air
                                       && samSitePosition.CalculateGreatCircleLine(unitPosition).Distance.SiValue <
                                       _samRanges[samSite.Type];
                            });

                            /*
                             * We should always be able to enable a SamSite because there might be a longer ranged radar in it. But we shouldn't shut down a sam site
                             * because one of the Radars is shorter range (i.e. shut down an SA-6 site because it has a Short Range Shilka in it)
                             */
                            if (targetsInRange > 0)
                            {
                                _logger.LogDebug("[{server}] {unitName} ({unitType}), {groupName}: {count} targets in activation range",
                                    GameServer.ShortName, samSite.Name, samSite.Type, samSite.GroupName, targetsInRange);
                                _logger.LogDebug("[{server}] {unitName} ({unitType}), {groupName}: Setting Alarm State to {alarmState}",
                                    GameServer.ShortName, samSite.Name, samSite.Type, samSite.GroupName, AlarmState.Red);
                                alarmStates[samSite.GroupName] = (int)AlarmState.Red;
                            }
                            else
                            {
                                _logger.LogDebug("[{server}] {unitName} ({unitType}), {groupName}: No targets in activation range",
                                    GameServer.ShortName, samSite.Name, samSite.Type, samSite.GroupName);
                                if (alarmStates.ContainsKey(samSite.GroupName))
                                {
                                    _logger.LogDebug("[{server}] {unitName} ({unitType}), {groupName}: Existing Alarm state set, skipping",
                                        GameServer.ShortName, samSite.Name, samSite.Type, samSite.GroupName);
                                }
                                else
                                {
                                    _logger.LogDebug("[{server}] {unitName} ({unitType}), {groupName}: Turning alarm state to {alarmState}",
                                        GameServer.ShortName, samSite.Name, samSite.Type, samSite.GroupName, AlarmState.Green);
                                    alarmStates[samSite.GroupName] = (int)AlarmState.Green;
                                }
                            }
                        }

                        foreach (var (groupName, alarmState) in alarmStates)
                        {
                            var unit = _units.Values.First(u => u.GroupName == groupName);
                            _logger.LogInformation("[{server}] {groupName}: Setting entire site alarm State to {alarmState}",
                                GameServer.ShortName, unit.GroupName, (AlarmState) alarmState);
                            unit.AlarmState = alarmState;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[{server}] Airspace monitoring failure", GameServer.ShortName);
                }
                await Task.Delay(10000, defenderToken);
            }
        }
    }
}
