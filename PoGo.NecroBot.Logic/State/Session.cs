#region using directives

using System;
using System.IO;
using GeoCoordinatePortable;
using PoGo.NecroBot.Logic.Common;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.Interfaces.Configuration;
using PoGo.NecroBot.Logic.Service;
using PoGo.NecroBot.Logic.Utils;
using PokemonGo.RocketAPI;
using POGOProtos.Networking.Responses;

#endregion

namespace PoGo.NecroBot.Logic.State
{
    public interface ISession
    {
        ISettings Settings { get; set; }
        Inventory Inventory { get; }
        Client Client { get; }
        GetPlayerResponse Profile { get; set; }
        Navigation Navigation { get; }
        ILogicSettings LogicSettings { get; }
        ITranslation Translation { get; }
        IEventDispatcher EventDispatcher { get; }
        TelegramService Telegram { get; set; }
        SessionStats Stats { get; }
    }


    public class Session : ISession
    {
        public Session(ISettings settings, ILogicSettings logicSettings)
        {
            Settings = settings;
            LogicSettings = logicSettings;
            EventDispatcher = new EventDispatcher();
            Translation = Common.Translation.Load(logicSettings);
            Reset(settings, LogicSettings);
            Stats = new SessionStats();
        }

        public ISettings Settings { get; set; }

        public Inventory Inventory { get; private set; }

        public Client Client { get; private set; }

        public GetPlayerResponse Profile { get; set; }
        public Navigation Navigation { get; private set; }

        public ILogicSettings LogicSettings { get; set; }

        public ITranslation Translation { get; }

        public IEventDispatcher EventDispatcher { get; }

        public TelegramService Telegram { get; set; }
        
        public SessionStats Stats { get; set; }

        public void Reset(ISettings settings, ILogicSettings logicSettings)
        {
            ApiFailureStrategy _apiStrategy = new ApiFailureStrategy(this);

            var lastPos = LoadPositionFromDisk(logicSettings);
            if (lastPos != null)
            {
                settings.DefaultLatitude = lastPos.Latitude;
                settings.DefaultLongitude = lastPos.Longitude;
                settings.DefaultAltitude = lastPos.Altitude;
            }

            Client = new Client(Settings, _apiStrategy);
            // ferox wants us to set this manually
            Inventory = new Inventory(Client, logicSettings);
            Navigation = new Navigation(Client, logicSettings);
        }

        private static GeoCoordinate LoadPositionFromDisk(ILogicSettings logicSettings)
        {
            if (
                File.Exists(Path.Combine(logicSettings.ProfileConfigPath, "LastPos.ini")) &&
                File.ReadAllText(Path.Combine(logicSettings.ProfileConfigPath, "LastPos.ini")).Contains(":"))
            {
                var latlngFromFile =
                    File.ReadAllText(Path.Combine(logicSettings.ProfileConfigPath, "LastPos.ini"));
                var latlng = latlngFromFile.Split(':');
                if (latlng[0].Length != 0 && latlng[1].Length != 0)
                {
                    try
                    {
                        var latitude = Convert.ToDouble(latlng[0]);
                        var longitude = Convert.ToDouble(latlng[1]);

                        if (Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180)
                        {
                            return new GeoCoordinate(latitude, longitude, LocationUtils.getElevation(latitude, longitude));
                        }

                        return null;
                    }
                    catch (FormatException)
                    {
                        return null;
                    }
                }
            }

            return null;
        }
    }
}