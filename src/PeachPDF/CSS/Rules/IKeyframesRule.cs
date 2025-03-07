﻿namespace PeachPDF.CSS
{
    public interface IKeyframesRule : IRule
    {
        string Name { get; set; }
        IRuleList Rules { get; }
        void Add(string rule);
        void Remove(string key);
        IKeyframeRule Find(string key);
    }
}