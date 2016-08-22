﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GeoCoordinatePortable;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.Model.Google;
using PoGo.NecroBot.Logic.Service;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Utils;
using PokemonGo.RocketAPI;
using POGOProtos.Networking.Responses;

namespace PoGo.NecroBot.Logic.Strategies.Walk
{
    class GoogleStrategy : IWalkStrategy
    {
        private readonly Client _client;
        public event UpdatePositionDelegate UpdatePositionEvent;

        private double CurrentWalkingSpeed = 0;
        private const double SpeedDownTo = 10 / 3.6;
        private DirectionsService _googleDirectionsService;
        private HumanStrategy _humanStraightLine;
        private readonly Random _randWalking = new Random();

        public GoogleStrategy(Client client)
        {
            _client = client;
        }

        public async Task<PlayerUpdateResponse> Walk(GeoCoordinate targetLocation, Func<Task<bool>> functionExecutedWhileWalking, ISession session, CancellationToken cancellationToken)
        {
            GetGoogleInstance(session);
            var sourceLocation = new GeoCoordinate(_client.CurrentLatitude, _client.CurrentLongitude, _client.CurrentAltitude);
            var googleResult = _googleDirectionsService.GetDirections(sourceLocation, new List<GeoCoordinate>(), targetLocation);

            if (googleResult.Directions.status.Equals("OVER_QUERY_LIMIT"))
            {
                return await RedirectToHumanStrategy(targetLocation, functionExecutedWhileWalking, session, cancellationToken);
            }
            session.EventDispatcher.Send(new NewPathToDestinyEvent { GoogleData = googleResult });

            PlayerUpdateResponse result = null;
            List<GeoCoordinate> points = googleResult.UncodedPath;
            Task<PlayerUpdateResponse> sendingData = null;
            var requestSendDatetime = new DateTime();

            foreach (var nextStep in points)
            {
                if (CurrentWalkingSpeed <= 0)
                    CurrentWalkingSpeed = session.LogicSettings.WalkingSpeedInKilometerPerHour;
                if (session.LogicSettings.UseWalkingSpeedVariant)
                    CurrentWalkingSpeed = session.Navigation.VariantRandom(session, CurrentWalkingSpeed);

                var speedInMetersPerSecond = CurrentWalkingSpeed / 3.6;

                var lastWalk = DateTime.Now;

                var realDistanceToTarget = sourceLocation.GetDistanceTo(targetLocation);
                if (realDistanceToTarget < _randWalking.Next(20, 40))
                {
                    return await _client.Player.UpdatePlayerLocation(sourceLocation.Latitude, sourceLocation.Longitude, sourceLocation.Altitude);
                }

                do
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var millisecondsUntilGetUpdatePlayerLocationResponse = (DateTime.Now - lastWalk).TotalMilliseconds;

                    var realDistanceToTargetSpeedDown = sourceLocation.GetDistanceTo(targetLocation);
                    if (realDistanceToTargetSpeedDown < 40)
                        if (speedInMetersPerSecond > SpeedDownTo)
                            speedInMetersPerSecond = SpeedDownTo;

                    if (session.LogicSettings.UseWalkingSpeedVariant)
                    {
                        CurrentWalkingSpeed = session.Navigation.VariantRandom(session, CurrentWalkingSpeed);
                        speedInMetersPerSecond = CurrentWalkingSpeed / 3.6;
                    }

                    var nextWaypointDistance = millisecondsUntilGetUpdatePlayerLocationResponse / 1000 * speedInMetersPerSecond;
                    var nextWaypointBearing = LocationUtils.DegreeBearing(sourceLocation, nextStep);
                    sourceLocation = LocationUtils.CreateWaypoint(sourceLocation, nextWaypointDistance, nextWaypointBearing);

                    // After a correct waypoint, get a random imprecise point in 5 meters around player - more realistic and prevent to walk on same line of Google Path

                    if (DateTime.Now.Subtract(requestSendDatetime).TotalSeconds > _randWalking.Next(8, 15))
                    {
                        var impreciseLocation = GenerateUnaccurateGeocoordinate(sourceLocation, nextWaypointBearing);
                        requestSendDatetime = DateTime.Now;
                        sendingData = _client.Player.UpdatePlayerLocation(impreciseLocation.Latitude, impreciseLocation.Longitude, impreciseLocation.Altitude);
                        session.EventDispatcher.Send(new UnaccurateLocation
                        {
                            Latitude = impreciseLocation.Latitude,
                            Longitude = impreciseLocation.Longitude
                        });
                    }
                    

                    
                    UpdatePositionEvent?.Invoke(sourceLocation.Latitude, sourceLocation.Longitude);
                    if (functionExecutedWhileWalking != null)
                        await functionExecutedWhileWalking(); // look for pokemon

                    lastWalk = DateTime.Now;
                    Thread.Sleep(1000);
                } while (sourceLocation.GetDistanceTo(nextStep) >= 3 ||
                         sourceLocation.GetDistanceTo(targetLocation) <= _randWalking.Next(30));


            }

            Task.WaitAll(sendingData);
            return await _client.Player.UpdatePlayerLocation(sourceLocation.Latitude, sourceLocation.Longitude, sourceLocation.Altitude);
        }

        /// <summary>
        /// Cell phones Gps systems can't generate accurate GEO, the average best they can is 5 meter.
        /// http://gis.stackexchange.com/questions/43617/what-is-the-maximum-theoretical-accuracy-of-gps
        /// </summary>
        public GeoCoordinate GenerateUnaccurateGeocoordinate(GeoCoordinate geo, double nextWaypointBearing)
        {

            var minBearing = Convert.ToInt32(nextWaypointBearing - 40);
            minBearing = minBearing > 0 ? minBearing : minBearing * -1;
            var maxBearing = Convert.ToInt32(nextWaypointBearing + 40);
            maxBearing = maxBearing < 360 ? maxBearing : 360 - maxBearing;

            var randomBearingDegrees = _randWalking.NextDouble() + _randWalking.Next(Math.Min(minBearing, maxBearing), Math.Max(minBearing, maxBearing));

            var randomDistance = _randWalking.NextDouble() * 3;

            return LocationUtils.CreateWaypoint(geo, randomDistance, randomBearingDegrees);
        }

        private Task<PlayerUpdateResponse> RedirectToHumanStrategy(GeoCoordinate targetLocation, Func<Task<bool>> functionExecutedWhileWalking, ISession session, CancellationToken cancellationToken)
        {
            if (_humanStraightLine == null)
                _humanStraightLine = new HumanStrategy(_client);

            return _humanStraightLine.Walk(targetLocation, functionExecutedWhileWalking, session, cancellationToken);
        }

        private void GetGoogleInstance(ISession session)
        {
            if (_googleDirectionsService == null)
                _googleDirectionsService = new DirectionsService(session);
        }
    }
}
