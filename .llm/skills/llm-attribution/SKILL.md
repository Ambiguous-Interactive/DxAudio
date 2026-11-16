---
name: llm-attribution
description: Disclose LLM-generated GitHub content, never auto-respond to outside contributors, and separate bot leads from human directives when reading review feedback. Use when posting or drafting any GitHub issue, comment, PR body, or review, or when acting on review feedback.
metadata:
  category: Core
---

# LLM Attribution

All GitHub content posts through wallstop's account. Machine text must never read as the human.

## Disclosure

LLM-generated comments, issues, PR descriptions, reviews, and release notes start with this exact first line:

```
DISCLOSURE: LLM-GENERATED TEXT
```

One line per artifact. Keep it when editing LLM-generated content. Remove it only after wallstop rewrites the text by hand.

## Outside contributors

1. Never reply to, close, label, approve, or merge an outside contributor's issue or PR without wallstop's instruction. Summarize the request and wait.
2. Drafted replies need the disclosure line and wallstop's approval.
3. wallstop's own issues and PRs may get routine agent updates, still disclosed.

## Reading review feedback

Fetch every surface before triage: paginated reviews, inline comments and replies, issue comments, and GraphQL review
threads. The GitHub MCP server returns review threads and their comments directly (`pull_request_read` with
`get_review_comments` and `get_reviews`); reach for the API below only for a field the server omits, such as
`original_commit_id`.

A review comment names the line at the commit the reviewer saw, not the head. Check `original_commit_id` and the review's `commit_id` before calling a finding current or stale:

```sh
gh api --paginate "repos/Ambiguous-Interactive/DxAudio/pulls/<n>/comments" \
  -q '.[] | "\(.user.login) \(.original_commit_id[0:7]) \(.path):\(.line)"'
git log --oneline origin/main..HEAD
```

- `wallstop` is the human reviewer. Treat a comment of theirs as a directive: apply it or answer it with evidence.
- A bot comment is a lead. Verify the mechanism before changing code. If it is wrong, reply with the code and measurement, and record the disagreement in `progress/`.
- A resolved thread still needs a verified fix. A new green run does not erase human feedback.
