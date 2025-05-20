using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace IqTest_server.Filters
{
    public class ModelValidationActionFilter : ActionFilterAttribute
    {
        private readonly ILogger<ModelValidationActionFilter> _logger;

        public ModelValidationActionFilter(ILogger<ModelValidationActionFilter> logger)
        {
            _logger = logger;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (!context.ModelState.IsValid)
            {
                var errors = context.ModelState
                    .Where(e => e.Value.Errors.Count > 0)
                    .Select(e => new
                    {
                        Name = e.Key,
                        Errors = e.Value.Errors.Select(error => error.ErrorMessage)
                    })
                    .ToList();

                _logger.LogError("Model validation failed for {ActionName}: {@Errors}", 
                    context.ActionDescriptor.DisplayName, errors);

                context.Result = new BadRequestObjectResult(new
                {
                    message = "Invalid request data",
                    errors = errors
                });
            }
        }
    }
}