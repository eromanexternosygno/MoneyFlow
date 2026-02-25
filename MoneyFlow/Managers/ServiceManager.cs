using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneyFlow.Context;
using MoneyFlow.Entities;
using MoneyFlow.Models;

namespace MoneyFlow.Managers;

public class ServiceManager(AppDbContext _dbContext)
{
    // This Class is a service manager and this is for: separating the logic from controller to manager

    // connect to the database using the dbContext and get all services for a user
    

    public List<ServiceViewModel> GetallServices(int userId)
    {
        var services = _dbContext.Service
            .Where(item => item.UserId == userId )
            //Implement Model for ServiceViewModel and map the entity to the model
            .Select(item => new ServiceViewModel
            {
                ServiceId = item.ServiceId,
                UserId = item.UserId,
                Name = item.Name,
                Type = item.Type
            })
            .ToList();

        return services;
    }

    
    // function to Save a new Service
    public int SaveNewService(ServiceViewModel sm)
    {
        sm.UserId = 1; // Temporary userId = 1, this should come from the session
        var entity = new Service
        {
            UserId = sm.UserId,
            Name = sm.Name,
            Type = sm.Type
        };

        try
        {
            _dbContext.Service.Add(entity);
            var result = _dbContext.SaveChanges();

            return result;
        }
        catch (Exception e)
        {
            // Log the exception (not implemented here)
            return 0;
        }
    }

    // function to get Service by Id
    public ServiceViewModel GetServiceById(int serviceId)
    {
        var service = _dbContext.Service
            .FirstOrDefault(sv => sv.ServiceId == serviceId);
        try
        {
            if (service != null)
            {
                return new ServiceViewModel
                {
                    ServiceId = service.ServiceId,
                    UserId = service.UserId,
                    Name = service.Name,
                    Type = service.Type
                };
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // function to Updtae Service
    public int UpdateService(ServiceViewModel sm)
    {
        var service = _dbContext.Service
            .FirstOrDefault(sv => sv.ServiceId == sm.ServiceId);
        try
        {
            if (service != null)
            {
                service.Name = sm.Name;
                service.Type = sm.Type;
                _dbContext.Service.Update(service);
                var result = _dbContext.SaveChanges();
                return result;
            }
            return 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // function to delete
    public int DelteTask(int serviceId)
    {
        var _entity = _dbContext.Service.Find(serviceId);

        try
        {
            if (_entity == null) throw new Exception();

            _dbContext.Service.Remove(_entity);
            return _dbContext.SaveChanges();
        }
        catch (Exception)
        {
            return 0;
        }
    }

}
