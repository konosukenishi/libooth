#nullable enable

using System;
using UnityEngine;

namespace Libooth.Editor
{
    /// <summary>Extracts one representative accent from image pixels.</summary>
    internal sealed class AccentColorExtractor
    {
        private readonly float _saturationWeight;
        private readonly float _populationWeight;

        /// <summary>Configures the relative saturation and population weights.</summary>
        public AccentColorExtractor(float saturationWeight, float populationWeight)
        {
            if (float.IsNaN(saturationWeight) || float.IsInfinity(saturationWeight) || saturationWeight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(saturationWeight));
            }

            if (float.IsNaN(populationWeight) || float.IsInfinity(populationWeight) || populationWeight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(populationWeight));
            }

            if (saturationWeight == 0 && populationWeight == 0)
            {
                throw new ArgumentException("Both saturationWeight and populationWeight cannot be zero.");
            }

            _saturationWeight = saturationWeight;
            _populationWeight = populationWeight;
        }

        /// <summary>Returns an accent, the dominant neutral color, or clear for an empty image.</summary>
        public Color Extract(Color32[] pixels)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));

            const int bucketCount = 16 * 16 * 16;
            var counts = new int[bucketCount];
            var red = new long[bucketCount];
            var green = new long[bucketCount];
            var blue = new long[bucketCount];

            var largestPopulation = 0;

            foreach (var pixel in pixels)
            {
                if (pixel.a < 128)
                {
                    continue;
                }

                var index = ((pixel.r >> 4) << 8) | ((pixel.g >> 4) << 4) | (pixel.b >> 4);

                counts[index]++;
                red[index] += pixel.r;
                green[index] += pixel.g;
                blue[index] += pixel.b;

                largestPopulation = Math.Max(largestPopulation, counts[index]);
            }

            if (largestPopulation == 0)
            {
                return Color.clear;
            }

            var dominantColor = Color.clear;
            var accentColor = Color.clear;
            var bestScore = float.NegativeInfinity;

            for (var i = 0; i < bucketCount; i++)
            {
                var count = counts[i];
                if (count == 0)
                {
                    continue;
                }

                var color = new Color(red[i] / (count * 255f), green[i] / (count * 255f), blue[i] / (count * 255f));

                if (count == largestPopulation) dominantColor = color;
                Color.RGBToHSV(color, out _, out var saturation, out var value);
                if (value < 0.15f || (value > 0.9f && saturation < 0.15f)
                    || count < largestPopulation * 0.02f) continue;
                var population = (float)count / largestPopulation;
                var score = saturation * _saturationWeight + population * _populationWeight;
                if (score > bestScore)
                {
                    bestScore = score;
                    accentColor = color;
                }
            }

            return float.IsNegativeInfinity(bestScore) ? dominantColor : accentColor;
        }
    }
}