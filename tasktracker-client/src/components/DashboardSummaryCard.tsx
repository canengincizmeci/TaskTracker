import { Link } from "react-router-dom";

type DashboardSummaryCardProps = {
  title: string;
  count: number;
  description: string;
  tone: "assigned" | "review" | "overdue" | "invitation";
  to?: string;
};

export default function DashboardSummaryCard({
  title,
  count,
  description,
  tone,
  to,
}: DashboardSummaryCardProps) {
  const content = <>
    <span className="dashboard-summary-card__marker" aria-hidden="true" />
    <span className="dashboard-summary-card__copy">
      <span className="dashboard-summary-card__label">{title}</span>
      <span className="dashboard-summary-card__description">{description}</span>
    </span>
    <strong className="dashboard-summary-card__count">{count}</strong>
    {to && <span className="dashboard-summary-card__action" aria-hidden="true">→</span>}
  </>;

  const className = `dashboard-summary-card dashboard-summary-card--${tone}${count === 0 ? " dashboard-summary-card--clear" : ""}`;

  return to ? (
    <Link className={className} to={to}
      aria-label={`${title}: ${count}. ${description}`}>
      {content}
    </Link>
  ) : (
    <article className={className}>
      {content}
    </article>
  );
}
