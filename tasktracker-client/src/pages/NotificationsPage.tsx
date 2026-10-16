import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import {
  getUserNotifications,
  markAllAsRead,
  markAsRead,
} from "../api/notificationService";
import { notificationHubConnection } from "../services/signalRService";
import type { Notification } from "../types/notification";
import InboxLocalNav from "../components/InboxLocalNav";

const UNREAD_COUNT_EVENT = "notification-unread-count-change";

function formatDate(value: string) {
  return new Date(value).toLocaleString(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  });
}

function NotificationsPage() {
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const [loading, setLoading] = useState(true);
  const [markingAll, setMarkingAll] = useState(false);
  const [failure, setFailure] = useState("");
  const markAllInFlight = useRef(false);
  const liveUnreadDuringMarkAll = useRef(new Set<number>());

  useEffect(() => {
    let isActive = true;

    const handleNotification = (notification: Notification) => {
      if (markAllInFlight.current && !notification.isRead) {
        liveUnreadDuringMarkAll.current.add(notification.id);
      }

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
        if (isActive) setFailure("Notifications could not be loaded.");
      } finally {
        if (isActive) setLoading(false);
      }
    };

    void loadNotifications();

    return () => {
      isActive = false;
      notificationHubConnection.off("ReceiveNotification", handleNotification);
    };
  }, []);

  const handleMarkAsRead = async (notificationId: number) => {
    try {
      await markAsRead(notificationId);
      const readAt = new Date().toISOString();

      setNotifications((currentNotifications) =>
        currentNotifications.map((notification) =>
          notification.id === notificationId
            ? { ...notification, isRead: true, readAt }
            : notification
        )
      );

      window.dispatchEvent(
        new CustomEvent(UNREAD_COUNT_EVENT, {
          detail: { notificationId },
        })
      );
    } catch (error) {
      console.error("Failed to mark notification as read:", error);
    }
  };

  const handleMarkAllAsRead = async () => {
    if (loading || markingAll || markAllInFlight.current) return;

    const unreadAtStart = new Set(
      notifications
        .filter((notification) => !notification.isRead)
        .map((notification) => notification.id)
    );

    markAllInFlight.current = true;
    liveUnreadDuringMarkAll.current.clear();
    setMarkingAll(true);

    try {
      await markAllAsRead();
      try {
        const refreshedNotifications = await getUserNotifications();
        const refreshedById = new Map(
          refreshedNotifications.map((notification) => [notification.id, notification])
        );
        const liveUnreadIds = new Set(liveUnreadDuringMarkAll.current);
        const unreadCandidates = new Set([
          ...unreadAtStart,
          ...liveUnreadIds,
        ]);

        unreadCandidates.forEach((notificationId) => {
          if (refreshedById.get(notificationId)?.isRead === true) {
            window.dispatchEvent(
              new CustomEvent(UNREAD_COUNT_EVENT, {
                detail: { notificationId },
              })
            );
          }
        });

        setNotifications((currentNotifications) => {
          const refreshedIds = new Set(
            refreshedNotifications.map((notification) => notification.id)
          );
          const liveAfterSnapshot = currentNotifications.filter(
            (notification) =>
              liveUnreadIds.has(notification.id) && !refreshedIds.has(notification.id)
          );

          return [...liveAfterSnapshot, ...refreshedNotifications];
        });
      } catch (refreshError) {
        console.error("Failed to refresh notifications after marking all as read:", refreshError);
        const readAt = new Date().toISOString();

        setNotifications((currentNotifications) =>
          currentNotifications.map((notification) =>
            unreadAtStart.has(notification.id)
              ? { ...notification, isRead: true, readAt }
              : notification
          )
        );

        unreadAtStart.forEach((notificationId) => {
          window.dispatchEvent(
            new CustomEvent(UNREAD_COUNT_EVENT, {
              detail: { notificationId },
            })
          );
        });
      }
    } catch (error) {
      console.error("Failed to mark all notifications as read:", error);
    } finally {
      markAllInFlight.current = false;
      liveUnreadDuringMarkAll.current.clear();
      setMarkingAll(false);
    }
  };

  const unreadCount = notifications.filter((notification) => !notification.isRead).length;

  return (
    <main className="page public-page inbox-page">
      <div className="inbox-shell">
        <header className="inbox-header">
          <div>
            <p className="eyebrow">INBOX</p>
            <h1>Inbox</h1>
            <p>Review updates and collaboration requests that need your attention.</p>
          </div>
          <div className="inbox-header__actions">
            <span className="inbox-count" aria-live="polite">{unreadCount} unread</span>
            <button type="button" className="secondary-button" onClick={() => void handleMarkAllAsRead()}
              disabled={loading || markingAll || unreadCount === 0}>
              {markingAll ? "Marking all..." : "Mark all as read"}
            </button>
          </div>
        </header>

        <InboxLocalNav />

        <section className="inbox-section" aria-labelledby="notifications-title">
          <div className="inbox-section__heading">
            <div><p className="eyebrow">UPDATES</p><h2 id="notifications-title">Notifications</h2></div>
            {!loading && !failure && <span>{notifications.length} total</span>}
          </div>

          {failure && <p role="alert" className="inbox-alert">{failure}</p>}
          {loading && <div className="inbox-state" role="status">Loading notifications...</div>}
          {!loading && !failure && notifications.length === 0 && <div className="inbox-state">
            <strong>No notifications</strong><span>Your notifications will appear here.</span></div>}
          {notifications.length > 0 && <ol className="inbox-list">
            {notifications.map((notification) => (
              <li className={`inbox-row${notification.isRead ? " inbox-row--read" : " inbox-row--unread"}`}
                key={notification.id}>
                <div className="inbox-row__marker" aria-hidden="true" />
                <div className="inbox-row__content">
                  <div className="inbox-row__title-line">
                    <h3>{notification.title}</h3>
                    <span className={`inbox-status${notification.isRead ? "" : " inbox-status--unread"}`}>
                      {notification.isRead ? "Read" : "Unread"}
                    </span>
                  </div>
                  <p>{notification.message}</p>
                  <time dateTime={notification.createdAt}>{formatDate(notification.createdAt)}</time>
                </div>
                <div className="inbox-row__actions">
                  {notification.redirectUrl && <Link className="primary-button" to={notification.redirectUrl}
                    aria-label={`Open ${notification.title}`} onClick={() => {
                      if (!notification.isRead) void handleMarkAsRead(notification.id);
                    }}>Open</Link>}
                  {!notification.isRead && <button type="button" className="secondary-button"
                    aria-label={`Mark ${notification.title} as read`}
                    onClick={() => void handleMarkAsRead(notification.id)}>Mark as read</button>}
                </div>
              </li>
            ))}
          </ol>}
        </section>
      </div>
    </main>
  );
}

export default NotificationsPage;
