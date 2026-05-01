using Xunit;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using System.Collections.Generic;
using System.Linq;
using System;

namespace TeruTeruPandas.Test;

public class Phase6VectorTests
{
    [Fact]
    public void VectorColumn_ShouldStoreAndCalculateSimilarities()
    {
        var col = new VectorColumn(2);
        float[] v1 = new[] { 1.0f, 0.0f };
        float[] v2 = new[] { 0.0f, 1.0f };
        
        col.SetValue(0, v1);
        col.SetValue(1, v2);

        float[] target = new[] { 1.0f, 1.0f };
        double[] sims = col.CalculateSimilarities(target);

        // Cosine similarity of [1,0] and [1,1] is 1/sqrt(2) approx 0.707
        Assert.Equal(0.707, sims[0], 3);
        Assert.Equal(0.707, sims[1], 3);

        float[] target2 = new[] { 1.0f, 0.0f };
        double[] sims2 = col.CalculateSimilarities(target2);
        Assert.Equal(1.0, sims2[0], 3);
        Assert.Equal(0.0, sims2[1], 3);
    }

    [Fact]
    public void DataFrame_ShouldOrderByCosineSimilarity()
    {
        var data = new Dictionary<string, IColumn>
        {
            ["Id"] = new PrimitiveColumn<int>(new[] { 1, 2, 3 }),
            ["Embedding"] = new VectorColumn(new[]
            {
                new[] { 1.0f, 0.0f }, // Very similar to [1, 0.1]
                new[] { 0.0f, 1.0f }, // Not similar
                new[] { 0.5f, 0.5f }  // Somewhat similar
            })
        };

        var df = new DataFrame(data);
        float[] target = new[] { 1.0f, 0.1f };

        var result = df.OrderByDescendingCosineSimilarity("Embedding", target);

        Assert.Equal(1, result["Id"].GetValue(0)); // Most similar first
        Assert.Equal(3, result["Id"].GetValue(1)); // Somewhat similar next
        Assert.Equal(2, result["Id"].GetValue(2)); // Least similar last
        
        Assert.True(Convert.ToDouble(result["Similarity"].GetValue(0)) > Convert.ToDouble(result["Similarity"].GetValue(1)));
    }
}
