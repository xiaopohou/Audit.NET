﻿#if NET45
using Audit.Core;
using Audit.Core.Extensions;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using System.Web.Http.ModelBinding;

namespace Audit.WebApi
{
    public class AuditApiAttribute : System.Web.Http.Filters.ActionFilterAttribute
    {
        /// <summary>
        /// Gets or sets a value indicating whether the output should include the Http Request Headers.
        /// </summary>
        public bool IncludeHeaders { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the output should include Model State information.
        /// </summary>
        public bool IncludeModelState { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the output should include the Http Response text.
        /// </summary>
        public bool IncludeResponseBody { get; set; }
        /// <summary>
        /// Gets or sets a string indicating the event type to use.
        /// Can contain the following placeholders:
        /// - {controller}: replaced with the controller name.
        /// - {action}: replaced with the action method name.
        /// - {verb}: replaced with the HTTP verb used (GET, POST, etc).
        /// </summary>
        public string EventTypeName { get; set; }

        private const string AuditApiActionKey = "__private_AuditApiAction__";
        private const string AuditApiScopeKey = "__private_AuditApiScope__";
        
        /// <summary>
        /// Occurs before the action method is invoked.
        /// </summary>
        /// <param name="actionContext">The action context.</param>
        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            var request = actionContext.Request;
            var contextWrapper = new ContextWrapper(request);
            var auditAction = new AuditApiAction
            {
                UserName = actionContext.RequestContext?.Principal?.Identity?.Name,
                IpAddress = contextWrapper.GetClientIp(),
                RequestUrl = request.RequestUri?.AbsoluteUri,
                HttpMethod = actionContext.Request.Method?.Method,
                FormVariables = contextWrapper.GetFormVariables(),
                Headers = IncludeHeaders ? ToDictionary(request.Headers) : null,
                ActionName = actionContext.ActionDescriptor?.ActionName,
                ControllerName = actionContext.ActionDescriptor?.ControllerDescriptor?.ControllerName,
                ActionParameters = actionContext.ActionArguments
            };
            var eventType = (EventTypeName ?? "{verb} {controller}/{action}").Replace("{verb}", auditAction.HttpMethod)
                .Replace("{controller}", auditAction.ControllerName)
                .Replace("{action}", auditAction.ActionName);
            // Create the audit scope
            var auditEventAction = new AuditEventWebApi()
            {
                Action = auditAction
            };
            var auditScope = AuditScope.Create(eventType, null, null, Configuration.CreationPolicy, null, auditEventAction);
            contextWrapper.Set(AuditApiActionKey, auditAction);
            contextWrapper.Set(AuditApiScopeKey, auditScope);
        }

        /// <summary>
        /// Occurs after the action method is invoked.
        /// </summary>
        /// <param name="actionExecutedContext">The action executed context.</param>
        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {
            var contextWrapper = new ContextWrapper(actionExecutedContext.Request);
            var auditAction = contextWrapper.Get<AuditApiAction>(AuditApiActionKey);
            var auditScope = contextWrapper.Get<AuditScope>(AuditApiScopeKey);
            if (auditAction != null && auditScope != null)
            {
                auditAction.Exception = actionExecutedContext.Exception.GetExceptionInfo();
                auditAction.ModelStateErrors = IncludeModelState ? GetModelStateErrors(actionExecutedContext.ActionContext.ModelState) : null;
                auditAction.ModelStateValid = IncludeModelState ? actionExecutedContext.ActionContext.ModelState?.IsValid : null;
                auditAction.ResponseBodyType = actionExecutedContext.Response?.Content?.GetType().Name;
                if (actionExecutedContext.Response != null)
                {
                    auditAction.ResponseStatus = actionExecutedContext.Response.ReasonPhrase;
                    auditAction.ResponseStatusCode = (int)actionExecutedContext.Response.StatusCode;
                    if (IncludeResponseBody)
                    {
                        var objContent = actionExecutedContext.Response.Content as ObjectContent;
                        auditAction.ResponseBody = objContent != null
                            ? new { Type = objContent.ObjectType.Name, Value = objContent.Value }
                            : (object)actionExecutedContext.Response.Content?.ReadAsStringAsync().Result;
                    }
                }
                else
                {
                    auditAction.ResponseStatusCode = 500;
                    auditAction.ResponseStatus = "Internal Server Error";
                }
                // Replace the Action field and save
                (auditScope.Event as AuditEventWebApi).Action = auditAction;
                auditScope.Save();
            }
        }

        private static IDictionary<string, string> ToDictionary(HttpRequestHeaders col)
        {
            if (col == null)
            {
                return null;
            }
            IDictionary<string, string> dict = new Dictionary<string, string>();
            foreach (var k in col)
            {
                dict.Add(k.Key, string.Join(", ", k.Value));
            }
            return dict;
        }

        private static IDictionary<string, string> ToDictionary(NameValueCollection col)
        {
            if (col == null)
            {
                return null;
            }
            IDictionary<string, string> dict = new Dictionary<string, string>();
            foreach (var k in col.AllKeys)
            {
                dict.Add(k, col[k]);
            }
            return dict;
        }

        private static Dictionary<string, string> GetModelStateErrors(ModelStateDictionary modelState)
        {
            if (modelState == null)
            {
                return null;
            }
            var dict = new Dictionary<string, string>();
            foreach (var state in modelState)
            {
                if (state.Value.Errors.Count > 0)
                {
                    dict.Add(state.Key, string.Join(", ", state.Value.Errors.Select(e => e.ErrorMessage)));
                }
            }
            return dict.Count > 0 ? dict : null;
        }

        internal static AuditScope GetCurrentScope(HttpRequestMessage request)
        {
            var contextWrapper = new ContextWrapper(request);
            return contextWrapper.Get<AuditScope>(AuditApiScopeKey);
        }
    }
}
#endif