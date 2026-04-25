import { useEffect, useState } from "react";
import { getAllTasks } from "../api/taskService";
import type { Task } from "../types/task";

function HomePage() {
  const [tasks, setTasks] = useState<Task[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const loadData = async () => {
      try {
        const data = await getAllTasks();
        setTasks(data);
      } catch (error) {
        console.log(error);
      } finally {
        setLoading(false);
      }
    };

    loadData();
  }, []);

  if (loading) return <div>Loading...</div>;

  return (
    <div style={{ padding: "30px" }}>
      <h1>Task Requests</h1>

      {tasks.map((task) => (
        <div
          key={task.id}
          style={{
            border: "1px solid gray",
            marginTop: "15px",
            padding: "15px",
            borderRadius: "10px"
          }}
        >
          <h3>{task.title}</h3>
          <p>{task.description}</p>
          <p>{task.category}</p>
          <p>{task.status}</p>
        </div>
      ))}
    </div>
  );
}

export default HomePage;