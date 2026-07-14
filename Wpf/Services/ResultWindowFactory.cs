using System;
using System.Collections.Generic;
using System.Text;
using Wpf.Services.IService;
using Wpf.ViewModels;
using Wpf.Views;

namespace Wpf.Services
{
    public class ResultWindowFactory : IResultWindowFactory
    {
        private readonly IImageProcessingService _processingService;

        // The DI container injects this once, the same way it injects any
        // other constructor dependency — no ServiceLocator/GetRequiredService
        // calls anywhere in this class.
        public ResultWindowFactory(IImageProcessingService processingService)
        {
            _processingService = processingService;
        }

        public ResultWindow Create(List<(string SessionId, string PreviewId, string FileName)> previewRefs)
        {
            var viewModel = new ResultViewModel(previewRefs, _processingService);
            return new ResultWindow(viewModel);
        }
    }
}
