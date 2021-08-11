﻿using System.Collections.Generic;
using System.Threading.Tasks;
using WeatherModule.Shared;

namespace WeatherModule.Server
{
    public interface IWeatherHub
    {
        public Task ReceiveWeatherUpdate(IEnumerable<WeatherForecast> weatherForecast);
    }
}
