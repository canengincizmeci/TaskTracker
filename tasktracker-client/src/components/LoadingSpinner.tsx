type LoadingSpinnerProps = {
  text?: string;
};

function LoadingSpinner({ text = "Loading..." }: LoadingSpinnerProps) {
  return (
    <div className="loading-spinner-wrapper">
      <div className="loading-spinner"></div>
      <p>{text}</p>
    </div>
  );
}

export default LoadingSpinner;