﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Ocelot.DownstreamRouteFinder.Finder;
using Ocelot.Logging;
using Ocelot.Middleware;
using Ocelot.OrleansHttpGateway.Model;
using Ocelot.Requester;
using Ocelot.Responses;

namespace Ocelot.OrleansHttpGateway.Requester
{
    public class DefaultOrleansRequester : IOrleansRequester
    {
        private readonly IClusterClientBuilder _clusterClientBuilder;
        private readonly IGrainReference _grainReference;
        private readonly IGrainMethodInvoker _grainInvoker;
        private readonly IRouteValuesBuilder _routeValuesBuilder;
        private readonly IOcelotLogger _logger;
        private readonly JsonSerializer _serializer;

        public DefaultOrleansRequester(IClusterClientBuilder clusterClientBuilder
            , IGrainReference grainReference
            , IGrainMethodInvoker grainInvoker
            , IRouteValuesBuilder routeValuesBuilder
            , IOcelotLoggerFactory factory
            , JsonSerializer jsonSerializer)
        {
            this._clusterClientBuilder = clusterClientBuilder;
            this._grainReference = grainReference;
            this._grainInvoker = grainInvoker;
            this._routeValuesBuilder = routeValuesBuilder;
            this._logger = factory.CreateLogger<DefaultOrleansRequester>();
            this._serializer = jsonSerializer;
        }
        public async Task<Response<OrleansResponseMessage>> GetResponse(DownstreamContext context)
        {
            try
            {
                var route = this._routeValuesBuilder.Build(context);
                _clusterClientBuilder.Build(route, context);
                GrainReference grain = this._grainReference.GetGrainReference(route);
                this.SetAuthorization(context.HttpContext);
                var result = await _grainInvoker.Invoke(grain, route);

                var content = new OrleansContent(result, this._serializer);
                var message = new OrleansResponseMessage(content, HttpStatusCode.OK);
                return new OkResponse<OrleansResponseMessage>(message);
            }
            catch (OrleansConfigurationException ex)
            {
                this._logger.LogError(nameof(OrleansConfigurationException), ex);
                return new ErrorResponse<OrleansResponseMessage>(new UnknownError("UnknownError"));
            }
            catch (OrleansGrainReferenceException ex)
            {
                this._logger.LogError(nameof(OrleansGrainReferenceException), ex);
                return new ErrorResponse<OrleansResponseMessage>(new UnknownError("UnknownError"));
            }
            catch (OrleansConnectionFailedException ex)
            {
                this._logger.LogError(nameof(OrleansConnectionFailedException), ex);
                return new ErrorResponse<OrleansResponseMessage>(new UnknownError("UnknownError"));
            }
            catch (OrleansRequestException ex)
            {
                this._logger.LogError(nameof(OrleansRequestException), ex);
                return new ErrorResponse<OrleansResponseMessage>(new UnknownError("UnknownError"));
            }
            catch (UnableToFindDownstreamRouteException ex)
            {
                this._logger.LogWarning(ex.Message);
                return new ErrorResponse<OrleansResponseMessage>(new UnableToFindDownstreamRouteError(context.DownstreamRequest.ToUri(), context.DownstreamRequest.Scheme));
            }
            catch (OrleansRequestTimedOutException ex)
            {
                this._logger.LogError(nameof(OrleansRequestTimedOutException), ex);
                return new ErrorResponse<OrleansResponseMessage>(new RequestTimedOutError(ex));
            }
            catch (Exception ex)
            {
                this._logger.LogError(nameof(DefaultOrleansRequester), ex);
                return new ErrorResponse<OrleansResponseMessage>(new UnknownError("UnknownError"));
            }
        }

        /// <summary>
        /// Set Authorization Bearer
        /// </summary>
        /// <param name="accessType"></param>
        private void SetAuthorization(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue("Authorization", out StringValues value))
                Orleans.Runtime.RequestContext.Set("Authorization", string.Format("Bearer {0}", value));
        }

    }
}
