# TaskTracker ML Data Foundation

## Goal

Prepare TaskTracker to collect production-quality data for a machine-learning course project whose first target is **deadline delay risk**.

The first ML milestone is not model training. It is building a reliable dataset from normal product usage without leaking future information into training features.

## Prediction question

At a chosen prediction time `T`, estimate whether an active task is at risk of missing the due date that was known at `T`.

Initial binary target:

- `1 = late`: task is completed after the due date that applied to the task at prediction time.
- `0 = on_time`: task is completed on or before that due date.

Tasks without a due date are excluded from the first model.

## Existing production data we can already use

### TaskRequest

- `Id`
- `OwnerId`
- `WorkspaceId`
- `AssigneeUserId`
- `Priority`
- `Status`
- `Category`
- `DueDate`
- `CreatedAt`
- `Version`

### TaskActivity (append-only)

Useful timestamps and workflow events already exist for:

- task creation
- assignment / unassignment
- work start
- submission
- changes requested
- approval
- completion
- cancellation
- reopen
- task details update

### Submission / review history

Immutable submissions and reviews provide:

- revision count
- submission timestamps
- review timestamps
- change-request count
- approval outcome

### Workspace context

Workspace and membership data can later provide collaboration/context features without needing message-content analysis.

## Critical gap before real data collection

`TaskDetailsUpdated` currently records that a task changed, but not **what planning values changed**.

For deadline-risk ML this matters because `TaskRequest.DueDate` and `Priority` contain only their latest values. If a user moves a deadline from 2026-10-01 to 2026-10-10, training code must not accidentally treat 2026-10-10 as if it were known before the change.

### Required production telemetry

Add an append-only planning-change record for task updates.

Suggested entity: `TaskPlanningChange`

Fields:

- `Id`
- `TaskRequestId`
- `ActorUserId`
- `CreatedAt`
- `TaskVersionBeforeChange`
- `DueDateChanged`
- `PreviousDueDate`
- `NewDueDate`
- `PriorityChanged`
- `PreviousPriority`
- `NewPriority`
- `CategoryChanged`
- `PreviousCategory`
- `NewCategory`

Do not store title or description copies in ML telemetry. They are not required for the first model and would unnecessarily duplicate user-authored text.

The row should be immutable after creation.

## Dataset design

The ML dataset should be generated from transactional data rather than maintained as a second editable source of truth.

One row represents one task at one historical prediction point.

Recommended first prediction point:

- immediately after the task is created, or
- immediately after assignment when a task has an assignee.

A later version can generate daily snapshots for active tasks.

### Candidate feature columns

Identity columns below are for joins/debugging and should not be direct model features:

- `task_id`
- `prediction_at`

Task features:

- `priority`
- `category`
- `is_workspace_task`
- `has_assignee`
- `task_age_hours`
- `days_until_due`
- `current_status`
- `deadline_change_count_before_t`
- `priority_change_count_before_t`
- `assignment_change_count_before_t`
- `activity_count_before_t`
- `work_started_before_t`
- `hours_from_creation_to_start` (when known at T)

Assignee-history features, calculated only from events before `T`:

- `assignee_active_task_count`
- `assignee_completed_task_count`
- `assignee_late_completion_rate`
- `assignee_avg_completion_hours`
- `assignee_changes_requested_rate`

Workspace features:

- `workspace_member_count`
- `workspace_active_task_count`

Later workflow features, for models run after work has started:

- `submission_count_before_t`
- `changes_requested_count_before_t`
- `hours_since_last_activity`
- `hours_since_work_started`

## Outcome / label generation

Use immutable workflow timestamps to determine completion time.

For each prediction row:

1. Reconstruct the due date that was known at `prediction_at` using task creation data plus planning-change history.
2. Find the first valid completion event after the prediction time.
3. If the task is cancelled, exclude it from the initial supervised dataset.
4. Compare completion time with the reconstructed due date.

Recommended label columns:

- `is_late`
- `completed_at`
- `due_date_at_prediction`
- `completion_delay_hours`

Do not write labels into transactional task rows.

## Leakage rules

A feature is valid only if it could have been known at prediction time.

Do not use:

- final task status as an early prediction feature
- final revision count
- future submissions or reviews
- future deadline changes
- future assignee performance
- completion timestamp
- outcome-derived fields

When splitting train/test data, split by task and preferably also evaluate a time-based split. Never allow snapshots from the same task to appear in both train and test sets.

## Privacy / product rules

- Do not use passwords, auth tokens, e-mail verification data, password reset data, IP addresses, or JWT information.
- Avoid raw task descriptions, messages, submission content, and review feedback in the first model.
- Prefer behavioural counts/timestamps and task metadata.
- User IDs may be used internally for joins, but exported course datasets should use pseudonymous IDs if individual identity is not required.
- ML data collection must not change authorization semantics or expose another user's task data through a public endpoint.

## Implementation plan

### Phase 1 — instrumentation

1. Add append-only `TaskPlanningChange`.
2. Write a row whenever due date, priority, or category changes.
3. Add EF Core configuration and migration.
4. Add persistence tests proving planning history cannot be modified/deleted.
5. Add tests proving unchanged fields do not generate false change flags.

### Phase 2 — dataset query/service

Create an internal dataset builder that reconstructs historical state from:

- `TaskRequests`
- `TaskActivities`
- `TaskPlanningChanges`
- `TaskSubmissions`
- `TaskSubmissionReviews`
- workspace/membership data

The first implementation can export CSV for the course project. Keep the export owner/admin-only or execute it offline; do not expose a public anonymous ML-data endpoint.

### Phase 3 — baseline model

Start with interpretable baselines:

- Logistic Regression
- Decision Tree
- Random Forest / Gradient Boosting comparison

Metrics:

- precision
- recall
- F1
- ROC-AUC
- confusion matrix

If late tasks are rare, report class balance and prefer precision/recall/F1 over accuracy alone.

### Phase 4 — product prediction

Only after enough production data exists:

- train model outside the .NET request path
- version the model
- expose inference through an internal service/API
- store prediction timestamp, model version, score, and feature-schema version
- show risk as decision support, not as an automatic task-state change

## Definition of ready-to-collect

TaskTracker is ML-data-ready when:

- task/workspace production flows remain stable
- deadline/priority/category history is reconstructable
- workflow events remain append-only
- completion labels can be derived without future-data leakage
- export/query logic has automated tests
- raw private text is not required for the first model
