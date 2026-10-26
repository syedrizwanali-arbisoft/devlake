# GitLab Dashboards

GitLab ports of the `GitHub Dashboards` folder. Each dashboard keeps its GitHub counterpart's layout and panel ids, and
reads only GitLab rows (`id LIKE 'gitlab:%'`). Every query is scoped through `project_mapping` and the viewer's
`user_project_mapping` rows.

Known issues and design decisions that are accepted as they are, plus deferred follow-ups, are listed in
[KNOWN_ISSUES.md](KNOWN_ISSUES.md).

| File | uid |
| --- | --- |
| A. GitLab High Level Summary | `gitlab-a-high-level-summary` |
| B. GitLab Contribution Overview | `gitlab-b-contribution-overview` |
| C. GitLab MR Health & Contributions | `gitlab-c-mr-health` |
| D. GitLab Issue Overview | `gitlab-d-issue-overview` |
| E. GitLab Issues Per Developer | `gitlab-e-issues-per-developer` |
| F. GitLab Delivery Metrics | `gitlab-f-delivery-metrics` |
| G. GitLab Work Distribution | `gitlab-g-work-distribution` |
| H. GitLab Basics | `gitlab-h-basics` |
| I. GitLab Classic | `gitlab-i-classic` |
| J. GitLab Legacy | `gitlab-j-legacy` |

## Metric definitions

These rules are shared by every dashboard. A panel that departs from one says so in its description.

| Term | Definition |
| --- | --- |
| Imported MR | An MR imported from GitHub: its description or one of its comments starts with `*Created by`, or the import account created it before the import date (the `not_imported_mr` constant). MR panels leave imported MRs out, and per-person review panels leave out reviews on them, because the import account approved those while importing. C's Monthly Contributions by MR keeps them, because an imported review is a real decision on its MR. |
| Draft | GitLab's `is_draft` is always 0, so a draft is an **open** MR whose title starts with `Draft:`, `[Draft]`, `(Draft)`, `WIP:` or `[WIP]`. A and C show drafts as their own slice. I and J leave open drafts out of their New MRs and status panels, and I has a Draft MRs row. |
| Bot | An account whose username matches `^(project\|group)_[0-9]+_bot_` (GitLab project and group access-token bots, e.g. the SonarQube integration). Bot-authored MRs and bot reviews are left out. On A, bot commits and pipelines count like any other author's. |
| Time to merge | Days from creation to merge, for MRs **merged** in the range (`merged_date > created_date`). Used by A's Average MR Merge Time, C's Merge Time Trend and I/J 4.5–4.6. |
| Pipeline result | `SUCCESS`, `FAILURE` (failed or canceled) or Unknown. Unknown means no final result yet: manual, running, pending or skipped pipelines. Manual pipelines did run; they lack a finished date only while they wait on a manual job. Pipelines are dated by finished date, or by created date while they have none. Success rates use final-result pipelines only. Durations use every pipeline with a recorded duration, manual ones included. |
| Child pipeline | `cicd_pipelines.is_child = 1`. Left out of every pipeline query, because a child runs as part of its parent. |
| Issue owner | An issue's assignees from `issue_assignees`, or `issues.assignee_id` when it has none (the `issue_owners` CTE). Used by A's issue panels (GitLab Issues Summary, Team Workload), D's Issue Type Mix Trend, E's Issues By Status and Issue Type Mix Per Developer, and every H panel, so A's issue tiles reconcile with their D and E drill-downs. D and E's other panels still use only `issues.assignee_id`. The `:0` placeholder account means unassigned. An issue with several assignees counts for each on per-person panels, and once on totals. |
| Commit author | GitLab accounts usually carry no email, so a commit author is matched to an account by email, then by the email's local part against the username, then by name against the username, then by a unique full name. J matches only accounts on the repo's own connection and keeps unmatched commits under the git author name. |
| Promotion MR | An MR from a branch that at least 20 MRs target (dev, stage, master, …). It carries every commit of the source branch, so MR size and failed-build panels leave it out. |
| Week start | The trend panels on C (Merge Time Trend, Time To First Review), D, E (Issues Closed) and F bucket with Grafana's `$__timeGroup(…, $interval)`, which counts fixed-length intervals from the Unix epoch. Their weeks start on Thursday, and their months don't line up with calendar months. I and J bucket by calendar: ISO weeks starting on Monday (labelled `W<week> <year>`) and calendar months. So a weekly number on C can differ from the same week on I. |
| Bug | An issue whose `original_type` is `INCIDENT` or that has a bug label (the `bug_issue` constant). Every other issue is a Task on A, and Non-bug on H. D and E split non-bugs further (Refactoring, Documentation, R&D, Reviews, Tasks, Unclassified) with label lists inline in their `issue_types` CTE. |
| In progress | GitLab issues are only open or closed, so an open issue is In Progress when it has an in-progress label (the `in_progress_issue` constant). Every other open issue is To Do. |
| Developer = All | A and J's developer variables use `allValue: "'__all__'"`, and their queries treat that value as "no filter". So All also counts authors who aren't in the list, such as commit-only contributors and unmatched commit authors. |

## Hidden constants

Org-specific SQL lives in hidden constant variables (`hide: 2`, `skipUrlSync: true`). Queries use them as
`${name:raw}`. Each constant uses a fixed table alias, noted in its label.

| Constant | Alias | Dashboards | Default |
| --- | --- | --- | --- |
| `not_imported_mr` | `pr` | A, B, C, G, I, J | `*Created by` description or comment, or created by `tayyab-razzaq` before `2021-07-01` |
| `bug_issue` | `i` | A, D, E, H | `INCIDENT`, or a label such as `bug`, `hotfix`, `prod issue`, `sentry` (see the constant for the full list) |
| `in_progress_issue` | `i` | A, E, J | `in progress`, `in review`, `in qa`, `ready to be merged (approved)`, `ready for deployment` |

To adapt them to another install:

1. Edit the constant in each dashboard JSON that has it. The value appears five times: `query`, `current.text`,
   `current.value`, `options[0].text` and `options[0].value`. Keep all five identical.
2. Keep each constant identical on every dashboard that carries it. Otherwise the same metric gives different numbers
   on different dashboards.
3. Typical edits:
   - `not_imported_mr`: replace the import account and date with your own, or drop the `NOT (pr.created_date < … )`
     part if you never imported from GitHub. The `*Created by` checks are harmless when nothing matches.
   - `bug_issue`: list your bug labels, lower-case. GitLab scoped labels match by full name, e.g. `type::bug`.
   - `in_progress_issue`: list your workflow labels, e.g. `workflow::in progress`.
4. Re-provision Grafana (provisioned dashboards can't be saved from the UI).

## Known GitLab data quirks

- **The `:0` placeholder.** Unassigned issues have `assignee_id = gitlab:GitlabAccount:<connection>:0`, an account row
  that carries a real person's name. Every issue query treats `:0` as unassigned.
- **Unapprove events.** GitLab has no "changes requested" review. An "unapproved this merge request" system note is
  stored as a NORMAL comment with `status = 'CHANGES_REQUESTED'`. The dashboards count it as a revoked approval or a
  rejection. Imported GitHub notes carry `**Review:** Approved` / `**Review:** Changes requested` bodies instead, and
  count the same way.
- **No account emails.** GitLab only returns account emails to tokens that may see them, so `accounts.email` is usually
  empty and commit authors are matched by username and name (see Commit author above).
- **Duplicate import-era notes.** 2,229 MR notes from before July 2021 are exact duplicates (same MR, account, body and
  timestamp), left by the GitHub import. They sit on imported MRs, which MR panels leave out. C's Monthly Contributions
  by MR counts MRs, not notes, so the duplicates don't inflate it.
- **Missing dates.** Merged MRs have no `closed_date`, so close time uses `merged_date`. Some old closed issues have no
  `resolution_date` and therefore no lead time; they are left out of lead-time and closed-per-interval panels.
- **No head commit on MRs.** `head_commit_sha` is never set for GitLab MRs, so I 7.2 shows each repo's current
  SonarQube coverage rather than coverage at the MR's commit.
