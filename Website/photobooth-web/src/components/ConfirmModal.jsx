import { useState } from 'react'

export default function ConfirmModal({ isOpen, onClose, onConfirm, title, message, confirmText = 'Download', cancelText = 'Cancel', isLoading = false }) {
  if (!isOpen) return null

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-icon">📥</span>
          <h3 className="modal-title">{title}</h3>
        </div>
        
        <div className="modal-body">
          <p className="modal-message">{message}</p>
        </div>
        
        <div className="modal-actions">
          <button 
            className="btn btn-secondary modal-btn" 
            onClick={onClose}
            disabled={isLoading}
          >
            {cancelText}
          </button>
          <button 
            className="btn btn-primary modal-btn" 
            onClick={onConfirm}
            disabled={isLoading}
          >
            {isLoading ? (
              <span className="modal-loading">
                <span className="loading-spinner"></span>
                Processing...
              </span>
            ) : (
              confirmText
            )}
          </button>
        </div>
      </div>
    </div>
  )
}

export function SuccessModal({ isOpen, onClose, title, message }) {
  if (!isOpen) return null

  return (
    <div className="modal-overlay success-modal-overlay" onClick={onClose}>
      <div className="modal-content success-modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-icon success-icon">✓</span>
          <h3 className="modal-title">{title}</h3>
        </div>
        
        <div className="modal-body">
          <p className="modal-message">{message}</p>
        </div>
        
        <div className="modal-actions">
          <button 
            className="btn btn-primary modal-btn" 
            onClick={onClose}
          >
            Done
          </button>
        </div>
      </div>
    </div>
  )
}

export function ErrorModal({ isOpen, onClose, title, message }) {
  if (!isOpen) return null

  return (
    <div className="modal-overlay error-modal-overlay" onClick={onClose}>
      <div className="modal-content error-modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-icon error-icon">!</span>
          <h3 className="modal-title">{title}</h3>
        </div>
        
        <div className="modal-body">
          <p className="modal-message">{message}</p>
        </div>
        
        <div className="modal-actions">
          <button 
            className="btn btn-secondary modal-btn" 
            onClick={onClose}
          >
            Close
          </button>
        </div>
      </div>
    </div>
  )
}
