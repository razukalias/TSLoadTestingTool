# Row Execution Settings

This document describes the current row-execution settings only.

## Settings

```json
{
  "RowsExecutionMode": "parallel",
  "MaxParallelRows": 3,
  "Iterations": 2,
  "ParallelRowStartIntervalMs": 1000,
  "IterationIntervalMs": 500
}
```

### RowsExecutionMode

Controls how selected data rows and their iterations are executed.

Allowed values:

- `parallel`
- `sequential`

#### `parallel`

Rows and iterations are scheduled as independent execution units. Several row/iteration executions may run at the same time, limited by `MaxParallelRows`.

#### `sequential`

Only one row/iteration execution runs at a time. The effective concurrency is always `1`, regardless of the value of `MaxParallelRows`.

The execution order is:

```text
Row 1 / Iteration 1
Row 1 / Iteration 2
Row 2 / Iteration 1
Row 2 / Iteration 2
...
```

## MaxParallelRows

Controls the maximum number of row/iteration executions running simultaneously in `parallel` mode.

It does not multiply with `Iterations`.

Example:

```json
{
  "RowsExecutionMode": "parallel",
  "MaxParallelRows": 3,
  "Iterations": 2
}
```

For 10 selected rows:

```text
Total executions = 10 rows × 2 iterations = 20 executions
Maximum simultaneous executions = 3
```

The runner starts up to three executions. When one finishes, the next pending execution starts. It does not run six executions simultaneously because `3 × 2` is not used as the concurrency limit.

## Iterations

Specifies how many times each selected row is executed.

Example:

```text
Rows = 4
Iterations = 3
Total executions = 4 × 3 = 12
```

`Iterations` increases the total amount of work, but it does not increase the concurrency limit.

In `parallel` mode, iterations are independent scheduling units. Therefore, the same row may have more than one iteration running at the same time if free parallel slots are available.

In `sequential` mode, the iterations of a row run one after another.

## ParallelRowStartIntervalMs

Adds a staggered startup delay between parallel worker positions.

Example:

```json
{
  "RowsExecutionMode": "parallel",
  "MaxParallelRows": 3,
  "ParallelRowStartIntervalMs": 1000
}
```

The approximate worker startup pattern is:

```text
Worker position 1: immediately
Worker position 2: approximately 1 second later
Worker position 3: approximately 2 seconds later
```

This setting helps avoid sending all initial requests at exactly the same moment. It is not a delay after every completed row.

It has no practical effect in `sequential` mode because only one worker is used.

## IterationIntervalMs

Controls the delay between iterations in `sequential` mode.

Example:

```json
{
  "RowsExecutionMode": "sequential",
  "Iterations": 3,
  "IterationIntervalMs": 500
}
```

The execution is approximately:

```text
Row 1 / Iteration 1
wait 500 ms
Row 1 / Iteration 2
wait 500 ms
Row 1 / Iteration 3
```

The last iteration does not wait before the next row starts.

This interval is used for sequential iteration spacing. In `parallel` mode, iterations are independent execution units and this setting does not serialize them.

## Complete examples

### Two executions of the same row in parallel

```json
{
  "RowsExecutionMode": "parallel",
  "MaxParallelRows": 2,
  "Iterations": 2
}
```

The two iterations of a row may run concurrently:

```text
Same row / Iteration 1 ─┐
                        ├─ concurrent
Same row / Iteration 2 ─┘
```

### Two executions of the same row sequentially

```json
{
  "RowsExecutionMode": "sequential",
  "MaxParallelRows": 100,
  "Iterations": 2,
  "IterationIntervalMs": 1000
}
```

The effective behavior is:

```text
Same row / Iteration 1
wait 1 second
Same row / Iteration 2
```

Even though `MaxParallelRows` is set to `100`, sequential mode allows only one active execution.

## Summary

```text
Total executions = selected rows × Iterations

Maximum simultaneous executions:
  parallel mode   = MaxParallelRows
  sequential mode = 1
```

`Iterations` controls how much work is performed. `MaxParallelRows` controls how much of that work may run at the same time.