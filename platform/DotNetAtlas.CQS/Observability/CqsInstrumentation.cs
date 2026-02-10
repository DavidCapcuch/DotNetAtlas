using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using DotNetAtlas.SharedKernel.Exceptions;

namespace DotNetAtlas.CQS.Observability;

public static class CqsInstrumentation
{
    private static readonly AssemblyName AssemblyName = typeof(CqsInstrumentation).Assembly.GetName();
    private static readonly string Version = AssemblyName.Version!.ToString();
    public static readonly string MeterName = AssemblyName.Name!;
    public static readonly string ActivitySourceName = AssemblyName.Name!;

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    private static readonly Meter Meter = new Meter(MeterName, Version);

    private static readonly Counter<long> CommandsTotal = Meter.CreateCounter<long>(
        "commands_total",
        description: "Total commands executed");

    private static readonly Counter<long> CommandErrorsTotal = Meter.CreateCounter<long>(
        "command_errors_total",
        description: "Total command errors by type and code");

    private static readonly Counter<long> CommandExceptionsTotal = Meter.CreateCounter<long>(
        "command_exceptions_total",
        description: "Total command exceptions by type");

    private static readonly Histogram<double> CommandDurationMs = Meter.CreateHistogram<double>(
        "command_duration_ms",
        unit: "ms",
        description: "Command execution duration in milliseconds");

    private static readonly Counter<long> QueriesTotal = Meter.CreateCounter<long>(
        "queries_total",
        description: "Total queries executed");

    private static readonly Counter<long> QueryErrorsTotal = Meter.CreateCounter<long>(
        "query_errors_total",
        description: "Total query errors by type and code");

    private static readonly Counter<long> QueryExceptionsTotal = Meter.CreateCounter<long>(
        "query_exceptions_total",
        description: "Total query exceptions by type");

    private static readonly Histogram<double> QueryDurationMs = Meter.CreateHistogram<double>(
        "query_duration_ms",
        unit: "ms",
        description: "Query execution duration in milliseconds");

    internal static void RecordCommandSuccess(string commandName, double durationMs)
    {
        CommandsTotal.Add(1, new TagList
        {
            {
                MetricsTags.CommandName, commandName
            },
            {
                MetricsTags.Status, MetricsTags.StatusSuccess
            }
        });
        CommandDurationMs.Record(durationMs, new TagList
        {
            {
                MetricsTags.CommandName, commandName
            },
            {
                MetricsTags.Status, MetricsTags.StatusSuccess
            }
        });
    }

    internal static void RecordCommandFailure(
        string commandName,
        double durationMs,
        IEnumerable<(string ErrorType, string ErrorCode)> errors)
    {
        CommandsTotal.Add(1, new TagList
        {
            {
                MetricsTags.CommandName, commandName
            },
            {
                MetricsTags.Status, MetricsTags.StatusFailed
            }
        });
        CommandDurationMs.Record(durationMs, new TagList
        {
            {
                MetricsTags.CommandName, commandName
            },
            {
                MetricsTags.Status, MetricsTags.StatusFailed
            }
        });

        foreach (var (errorType, errorCode) in errors)
        {
            CommandErrorsTotal.Add(1, new TagList
            {
                {
                    MetricsTags.CommandName, commandName
                },
                {
                    MetricsTags.ErrorType, errorType
                },
                {
                    MetricsTags.ErrorCode, errorCode
                }
            });
        }
    }

    internal static void RecordCommandException(string commandName, double durationMs, Exception ex)
    {
        CommandsTotal.Add(1, new TagList
        {
            {
                MetricsTags.CommandName, commandName
            },
            {
                MetricsTags.Status, MetricsTags.StatusException
            }
        });
        CommandDurationMs.Record(durationMs, new TagList
        {
            {
                MetricsTags.CommandName, commandName
            },
            {
                MetricsTags.Status, MetricsTags.StatusException
            }
        });
        CommandExceptionsTotal.Add(1, new TagList
        {
            {
                MetricsTags.CommandName, commandName
            },
            {
                MetricsTags.ExceptionType, ex.GetType().Name
            },
            {
                MetricsTags.IsCritical, ex is CriticalException
            }
        });
    }

    internal static void RecordQuerySuccess(string queryName, double durationMs)
    {
        QueriesTotal.Add(1, new TagList
        {
            {
                MetricsTags.QueryName, queryName
            },
            {
                MetricsTags.Status, MetricsTags.StatusSuccess
            }
        });
        QueryDurationMs.Record(durationMs, new TagList
        {
            {
                MetricsTags.QueryName, queryName
            },
            {
                MetricsTags.Status, MetricsTags.StatusSuccess
            }
        });
    }

    internal static void RecordQueryFailure(
        string queryName,
        double durationMs,
        IEnumerable<(string ErrorType, string ErrorCode)> errors)
    {
        QueriesTotal.Add(1, new TagList
        {
            {
                MetricsTags.QueryName, queryName
            },
            {
                MetricsTags.Status, MetricsTags.StatusFailed
            }
        });
        QueryDurationMs.Record(durationMs, new TagList
        {
            {
                MetricsTags.QueryName, queryName
            },
            {
                MetricsTags.Status, MetricsTags.StatusFailed
            }
        });

        foreach (var (errorType, errorCode) in errors)
        {
            QueryErrorsTotal.Add(1, new TagList
            {
                {
                    MetricsTags.QueryName, queryName
                },
                {
                    MetricsTags.ErrorType, errorType
                },
                {
                    MetricsTags.ErrorCode, errorCode
                }
            });
        }
    }

    internal static void RecordQueryException(string queryName, double durationMs, Exception ex)
    {
        QueriesTotal.Add(1, new TagList
        {
            {
                MetricsTags.QueryName, queryName
            },
            {
                MetricsTags.Status, MetricsTags.StatusException
            }
        });
        QueryDurationMs.Record(durationMs, new TagList
        {
            {
                MetricsTags.QueryName, queryName
            },
            {
                MetricsTags.Status, MetricsTags.StatusException
            }
        });
        QueryExceptionsTotal.Add(1, new TagList
        {
            {
                MetricsTags.QueryName, queryName
            },
            {
                MetricsTags.ExceptionType, ex.GetType().Name
            },
            {
                MetricsTags.IsCritical, ex is CriticalException
            }
        });
    }
}
