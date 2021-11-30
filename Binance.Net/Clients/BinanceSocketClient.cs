﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Binance.Net.Converters;
using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Interfaces.Clients;
using Binance.Net.Interfaces.Clients.Socket;
using Binance.Net.Objects;
using Binance.Net.Objects.Internal;
using Binance.Net.Objects.Models;
using Binance.Net.Objects.Models.Spot;
using Binance.Net.Objects.Models.Spot.Socket;
using CryptoExchange.Net;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Binance.Net.Clients.Socket
{
    /// <summary>
    /// Client providing access to the Binance Spot websocket Api
    /// </summary>
    public class BinanceSocketClient : SocketClient, IBinanceSocketClient
    {
        #region fields
        #endregion

        #region Subclients

        public IBinanceSocketClientSpotMarket SpotMarket { get; set; }
        public IBinanceSocketClientUsdFuturesMarket UsdFuturesMarket { get; set; }
        public IBinanceSocketClientCoinFuturesMarket CoinFuturesMarket { get; set; }

        #endregion

        #region constructor/destructor

        /// <summary>
        /// Create a new instance of BinanceSocketClientSpot with default options
        /// </summary>
        public BinanceSocketClient() : this(BinanceSocketClientOptions.Default)
        {
        }

        /// <summary>
        /// Create a new instance of BinanceSocketClientSpot using provided options
        /// </summary>
        /// <param name="options">The options to use for this client</param>
        public BinanceSocketClient(BinanceSocketClientOptions options) : base("Binance", options)
        {
            SetDataInterpreter((byte[] data) => string.Empty, null);
            RateLimitPerSocketPerSecond = 4;

            SpotMarket = new BinanceSocketClientSpot(log, this, options);
            UsdFuturesMarket = new BinanceSocketClientUsdFutures(log, this, options);
            CoinFuturesMarket = new BinanceSocketClientCoinFutures(log, this, options);
        }
        #endregion 

        #region methods

        /// <summary>
        /// Set the default options to be used when creating new BinanceSocketClientSpot instances
        /// </summary>
        /// <param name="options"></param>
        public static void SetDefaultOptions(BinanceSocketClientOptions options)
        {
            BinanceSocketClientOptions.Default = options;
        }

        internal CallResult<T> DeserializeInternal<T>(JToken obj, JsonSerializer? serializer = null, int? requestId = null)
            => Deserialize<T>(obj, serializer, requestId);

        internal Task<CallResult<UpdateSubscription>> SubscribeInternal<T>(SocketSubClient subClient, string url, IEnumerable<string> topics, Action<DataEvent<T>> onData, CancellationToken ct)
        {
            var request = new BinanceSocketRequest
            {
                Method = "SUBSCRIBE",
                Params = topics.ToArray(),
                Id = NextId()
            };

            return SubscribeAsync(subClient, url.AppendPath("stream"), request, null, false, onData, ct);
        }

        /// <inheritdoc />
        protected override bool HandleQueryResponse<T>(SocketConnection s, object request, JToken data, out CallResult<T> callResult)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override bool HandleSubscriptionResponse(SocketConnection s, SocketSubscription subscription, object request, JToken message, out CallResult<object>? callResult)
        {
            callResult = null;
            if (message.Type != JTokenType.Object)
                return false;

            var id = message["id"];
            if (id == null)
                return false;

            var bRequest = (BinanceSocketRequest)request;
            if ((int)id != bRequest.Id)
                return false;

            var result = message["result"];
            if (result != null && result.Type == JTokenType.Null)
            {
                log.Write(LogLevel.Trace, $"Socket {s.Socket.Id} Subscription completed");
                callResult = new CallResult<object>(null, null);
                return true;
            }

            var error = message["error"];
            if (error == null)
            {
                callResult = new CallResult<object>(null, new ServerError("Unknown error: " + message));
                return true;
            }

            callResult = new CallResult<object>(null, new ServerError(error["code"]!.Value<int>(), error["msg"]!.ToString()));
            return true;
        }

        /// <inheritdoc />
        protected override bool MessageMatchesHandler(JToken message, object request)
        {
            if (message.Type != JTokenType.Object)
                return false;

            var bRequest = (BinanceSocketRequest)request;
            var stream = message["stream"];
            if (stream == null)
                return false;

            return bRequest.Params.Contains(stream.ToString());
        }

        /// <inheritdoc />
        protected override bool MessageMatchesHandler(JToken message, string identifier)
        {
            return true;
        }

        /// <inheritdoc />
        protected override Task<CallResult<bool>> AuthenticateSocketAsync(SocketConnection s)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override async Task<bool> UnsubscribeAsync(SocketConnection connection, SocketSubscription subscription)
        {
            var topics = ((BinanceSocketRequest)subscription.Request!).Params;
            var unsub = new BinanceSocketRequest { Method = "UNSUBSCRIBE", Params = topics, Id = NextId() };
            var result = false;

            if (!connection.Socket.IsOpen)
                return true;

            await connection.SendAndWaitAsync(unsub, ClientOptions.SocketResponseTimeout, data =>
            {
                if (data.Type != JTokenType.Object)
                    return false;

                var id = data["id"];
                if (id == null)
                    return false;

                if ((int)id != unsub.Id)
                    return false;

                var result = data["result"];
                if (result?.Type == JTokenType.Null)
                {
                    result = true;
                    return true;
                }

                return true;              
            }).ConfigureAwait(false);
            return result;
        }

        public override void Dispose()
        {
            SpotMarket.Dispose();
            UsdFuturesMarket.Dispose();
            CoinFuturesMarket.Dispose();
            base.Dispose();
        }
        #endregion
    }
}
