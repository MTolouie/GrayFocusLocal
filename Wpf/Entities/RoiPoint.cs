using System;
using System.Collections.Generic;
using System.Text;

namespace Wpf.Entities;

public readonly struct RoiPoint
{
    public double X { get; }
    public double Y { get; }

    public RoiPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}
