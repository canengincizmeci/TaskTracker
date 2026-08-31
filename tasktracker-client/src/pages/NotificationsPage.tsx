import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getUserNotifications } from "../api/notificationService";
import { notificationHubConnection } from "../services/signalRService";
import type { Notification } from "../types/notification";

function NotificationsPage() {
  const [notifications, setNotifications] = useState<Notification[]>([]);

  useEffect(() => {
    let isActive = true;

    const handleNotification = (notification: Notification) => {
      setNotifications((currentNotifications) =>
        currentNotifications.some((item) => item.id === notification.id)
          ? currentNotifications
          : [notification, ...currentNotifications]
      );
    };

    notificationHubConnection.on("ReceiveNotification", handleNotification);

    const loadNotifications = async () => {
      try {
        const initialNotifications = await getUserNotifications();

        if (isActive) {
          setNotifications((currentNotifications) => {
            const currentIds = new Set(
              currentNotifications.map((notification) => notification.id)
            );

            return [
              ...currentNotifications,
              ...initialNotifications.filter(
                (notification) => !currentIds.has(notification.id)
              ),
            ];
          });
        }
      } catch (error) {
        console.error("Failed to load notifications:", error);
      }
    };

    void loadNotifications();

    return () => {
      isActive = false;
      notificationHubConnection.off("ReceiveNotification", handleNotification);
    };
  }, []);

  return (
    <main className="page public-page">
      <section className="task-detail-layout">
        <div className="task-detail-main">
          <div className="task-detail-header">
            <div>
              <p className="eyebrow">NOTIFICATIONS</p>
              <h1>Notification Center</h1>
            </div>

            <div className="task-detail-actions">
              <button type="button" className="secondary-button">
                Mark all as read
              </button>
            </div>
          </div>

          <p className="task-detail-description">
            Task invitations, updates and system notifications will appear here.
          </p>

          <section className="task-detail-section">
            <div className="task-section-header">
              <div>
                <p className="eyebrow">RECENT</p>
                <h2>Your notifications</h2>
              </div>
            </div>

            <div className="activity-timeline">
              {notifications.length === 0 ? (
                <div className="timeline-item">
                  <strong>No notifications</strong>
                  <span>Your notifications will appear here.</span>
                </div>
              ) : (
                notifications.map((notification) => (
                  <div className="timeline-item" key={notification.id}>
                    <strong>{notification.title}</strong>
                    <span>{notification.message}</span>
                  </div>
                ))
              )}
            </div>
          </section>
        </div>

        <aside className="task-detail-sidebar">
          <div className="task-sidebar-card">
            <div className="task-sidebar-header">
              <p className="eyebrow">QUICK ACCESS</p>
              <h2>Workspace links</h2>
            </div>

            <div className="task-sidebar-links">
              <Link to="/tasks/user-tasks">My Tasks</Link>
              <Link to="/tasks/create-task">Create Task</Link>
              <Link to="/profile">Profile</Link>
            </div>
          </div>
        </aside>
      </section>
    </main>
  );
}

export default NotificationsPage;
