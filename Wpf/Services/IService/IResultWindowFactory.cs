using System;
using System.Collections.Generic;
using System.Text;
using Wpf.Views;

namespace Wpf.Services.IService
{
    public interface IResultWindowFactory
    {
        public ResultWindow Create(List<(string SessionId, string PreviewId, string FileName)> previewRefs);
    }
}
