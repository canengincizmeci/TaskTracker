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
    <span className="dashboard-summary-card__label">{title}</span>
    <strong className="dashboard-summary-card__count">{count}</strong>
    <span className="dashboard-summary-card__description">{description}</span>
    {to && <span className="dashboard-summary-card__action">Open view <span aria-hidden="true">→</span></span>}
  </>;

  return to ? (
    <Link className={`dashboard-summary-card dashboard-summary-card--${tone}`} to={to}
      aria-label={`${title}: ${count}. ${description}`}>
      {content}
    </Link>
  ) : (
    <article className={`dashboard-summary-card dashboard-summary-card--${tone}`}>
      {content}
    </article>
  );
}
