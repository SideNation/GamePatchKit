// C# Coding Standards — consolidated example
// - Opening brace on a new line; else/catch/finally on a new line
// - Members of object/anonymous-type initializers on separate lines
// - One blank line after an `if` block
// - LINQ query clauses stay on a single line
// - `switch` case labels and contents are indented; ordinary labels are flush-left
// - No multiple statements per line; single-line blocks (auto properties) are preserved

using System;
using System.Collections.Generic;
using System.Linq;

namespace MyApp.Examples;

public class CodingStyleExample
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Count { get; private set; }

    public void Run(IEnumerable<int> source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var snapshot = source.ToList();

        var order = new Order
        {
            Id = 1,
            CustomerName = "Alice",
            Status = "Pending"
        };

        var anonymous = new
        {
            Id = order.Id,
            Name = order.CustomerName
        };

        try
        {
            Process(snapshot);
        }
        catch (InvalidOperationException)
        {
            Count = 0;
        }
        finally
        {
            Console.WriteLine("done");
        }

        var evens = from n in snapshot where n % 2 == 0 select n;
        Console.WriteLine(evens.Count());
    }

    public string Describe(int code)
    {
        switch (code)
        {
            case 0:
                return "zero";
            case 1:
            {
                var temp = "one";
                return temp;
            }
            default:
                return "unknown";
        }
    }

    public void GotoSample(int n)
    {
        if (n <= 0)
        {
            goto End;
        }

        Console.WriteLine(n);

End:
        Console.WriteLine("end");
    }

    private void Process(List<int> values)
    {
        foreach (var value in values)
        {
            if (value < 0)
            {
                continue;
            }

            Count += value;
        }
    }

    private class Order
    {
        public int Id { get; set; }
        public string CustomerName { get; set; }
        public string Status { get; set; }
    }
}
